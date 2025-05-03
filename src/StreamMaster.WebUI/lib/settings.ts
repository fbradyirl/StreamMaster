import { Logger } from "@lib/common/logger";

export const isClient = typeof window !== "undefined";
export const isDev = process.env.NODE_ENV === "development";

interface AppConfig {
	defaultPort: number;
	defaultBaseUrl: string;
}

const defaultConfig: AppConfig = {
	defaultPort: 7095,
	defaultBaseUrl: "",
};

let configPromise: Promise<AppConfig> | null = null;
let configData: AppConfig | null = null;

/**
 * Dynamically determines the base path of the application
 */
export const getBasePath = () => {
	if (!isClient) return "/";

	try {
		// Get the URL of the current module
		// https://vite.dev/guide/assets#new-url-url-import-meta-url
		const currentUrl = new URL(import.meta.url);

		// Module URL typically points to /assets/[hash].js
		// We need to go up to the root directory...
		const pathParts = currentUrl.pathname.split("/");

		// Find where the assets directory is
		const assetsIndex = pathParts.findIndex((part) => part === "assets");

		if (assetsIndex !== -1) {
			// Remove everything from assets onwards to get the app's root path
			const basePath = `${pathParts.slice(0, assetsIndex).join("/")}/`;
			return basePath;
		}

		// Fallback if structure is different
		Logger.warn("Could not determine base path from module URL");
		return "/";
	} catch (error) {
		Logger.error("Error determining base path:", error);
		return "/";
	}
};

/**
 * Loads the configuration file using fetch
 * Ensures the path is relative to the application root
 */
export const loadConfig = async (): Promise<AppConfig> => {
	if (configData) {
		return configData;
	}

	if (!configPromise) {
		configPromise = (async (): Promise<AppConfig> => {
			if (!isClient) {
				Logger.debug(
					"Config not loaded in server environment, using defaults.",
				);
				return defaultConfig;
			}

			try {
				// Get the dynamically determined base path
				const basePath = getBasePath();

				// Join the base path with config.json
				const configPath = `${basePath}config.json`;

				const response = await fetch(configPath);

				if (!response.ok) {
					Logger.error(`Failed to load config: ${response.status}`);
					throw new Error(`Failed to load config: ${response.status}`);
				}

				const data = (await response.json()) as AppConfig;
				configData = {
					...defaultConfig,
					...data,
				};
				return configData;
			} catch (error) {
				Logger.error("Config loading error:", error);
				configData = defaultConfig;
				return configData;
			}
		})();
	}

	return configPromise;
};

/**
 * Waits for configuration to be loaded before proceeding
 */
export const waitForConfig = async (): Promise<AppConfig> => {
	if (!isClient) {
		return defaultConfig;
	}
	return loadConfig();
};

/**
 * Gets the base URL for API requests
 */
export const getBaseHostURL = async (): Promise<string> => {
	const config = await loadConfig();

	return isClient && !isDev
		? `${window.location.protocol}//${window.location.host}`
		: `http://localhost:${config.defaultPort}${config.defaultBaseUrl}`;
};

// For backward compatibility - these will be initialized after config loads
export let defaultPort: number = defaultConfig.defaultPort;
export let defaultBaseUrl: string = defaultConfig.defaultBaseUrl;
export let baseHostURL = "";

// Initialize the values if we're in a client environment
if (isClient) {
	loadConfig().then((config) => {
		defaultPort = config.defaultPort;
		defaultBaseUrl = config.defaultBaseUrl;

		baseHostURL = !isDev
			? `${window.location.protocol}//${window.location.host}`
			: `http://localhost:${defaultPort}${defaultBaseUrl}`;
	});
}

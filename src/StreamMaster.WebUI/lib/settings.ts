// settings.ts

export const isClient = typeof window !== "undefined";

export const isDev = process.env.NODE_ENV === "development";

const basePath = window.location.pathname.split('/').slice(0, -1).join('/');

const loadConfig = () => {
	const request = new XMLHttpRequest();
	request.open("GET", `${basePath}/config.json`, false); // use dynamic base path. Synchronous request
	request.send(null);

	if (request.status === 200) {
		return JSON.parse(request.responseText);
	} else {
		throw new Error(`Failed to load config: ${request.status}`);
	}
};

const config = loadConfig();

export const defaultPort = config.defaultPort;
export const defaultBaseUrl = config.defaultBaseUrl;

export const baseHostURL =
	isClient && !isDev
		? `${window.location.origin}${window.location.pathname.replace(/\/$/, '')}`
		: `http://localhost:${defaultPort}${defaultBaseUrl}`;

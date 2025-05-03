import { Logger } from "@lib/common/logger";
import { waitForConfig } from "@lib/settings";
import { useState, useEffect } from "react";

export const ConfigLoader = ({ children }: { children: React.ReactNode }) => {
	const [configLoaded, setConfigLoaded] = useState(false);
	const [error, setError] = useState<string | null>(null);

	useEffect(() => {
		waitForConfig()
			.then(() => setConfigLoaded(true))
			.catch((error) => {
				Logger.error("Failed to load dynamic config:", error);
				setError(error);
				setConfigLoaded(true);
			});
	}, []);

	if (!configLoaded) {
		return <div>Loading application configuration...</div>;
	}

	if (error) {
		return <div>Error loading application configuration: {error}</div>;
	}

	return <>{children}</>;
};

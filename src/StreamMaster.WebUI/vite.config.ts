import react from "@vitejs/plugin-react";
import path from "node:path";
import { visualizer } from "rollup-plugin-visualizer";
import { fileURLToPath } from "node:url";

import { builtinModules } from "module";
import { defineConfig } from "vite";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

console.log("Vite __dirname:", __dirname);

export default defineConfig({
	appType: "spa",
	base: "./",
	build: {
		emptyOutDir: true,
		rollupOptions: {
			external: builtinModules,
			output: {
				assetFileNames: "assets/[name].[hash][extname]",
				chunkFileNames: "assets/[name].[hash].js",
				entryFileNames: "assets/[name].[hash].js",
				manualChunks: (id: any): string | undefined => {
					console.log(id);
					if (id.includes("node_modules")) {
						return id
							.toString()
							.split("node_modules/")[1]
							.split("/")[0]
							.toString();
					}

					if (id.includes("/StreamMaster.WebUI/lib/smAPI/")) {
						return "smAPI";
					}
					if (id.includes("/StreamMaster.WebUI/lib/")) {
						return "smLib";
					}
					if (id.includes("/StreamMaster.WebUI/components/")) {
						return "smComponents";
					}
					if (id.includes("/StreamMaster.WebUI/features/")) {
						return "smFeatures";
					}
					return undefined;
				},
			},
		},
	},
	clearScreen: true,
	plugins: [react(), visualizer()],
	resolve: {
		alias: {
			"@": path.resolve(__dirname),
			"@components": path.resolve(__dirname, "components"),
			"@features": path.resolve(__dirname, "features"),
			"@lib": path.resolve(__dirname, "lib"),
		},
	},
});

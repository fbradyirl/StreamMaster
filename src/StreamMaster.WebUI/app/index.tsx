import store, { persistor } from "@lib/redux/store";
import { PrimeReactProvider } from "primereact/api";
import React from "react";
import ReactDOM from "react-dom/client";
import { Provider } from "react-redux";
import { PersistGate } from "redux-persist/integration/react";
import App from "./App";
import { ConfigLoader } from "./ConfigLoader";

const root = ReactDOM.createRoot(
	document.querySelector("#root") as HTMLElement,
);

root.render(
	<React.StrictMode>
		<ConfigLoader>
			<Provider store={store}>
				<PersistGate persistor={persistor}>
					<PrimeReactProvider value={{ inputStyle: "outlined", ripple: false }}>
						<App />
					</PrimeReactProvider>
				</PersistGate>
			</Provider>
		</ConfigLoader>
	</React.StrictMode>,
);

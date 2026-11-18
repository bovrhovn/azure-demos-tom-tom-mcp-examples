import { createApp } from "vue";
import "@fontsource-variable/manrope";
import "./style.css";
import ChatApp from "./ChatApp.vue";
import MapsApp from "./MapsApp.vue";
import TravelApp from "./TravelApp.vue";

const workspace = document.querySelector("#app")?.dataset;
const component = workspace?.workspace === "travel" ? TravelApp : workspace?.workspace === "maps" ? MapsApp : ChatApp;
createApp(component, component === TravelApp ? { scenario: workspace.scenario ?? "" } : {}).mount("#app");

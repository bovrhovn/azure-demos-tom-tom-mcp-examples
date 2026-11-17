import { createApp } from "vue";
import "@fontsource-variable/manrope";
import "./style.css";
import ChatApp from "./ChatApp.vue";
import MapsApp from "./MapsApp.vue";

createApp(document.querySelector("#app")?.dataset.workspace === "maps" ? MapsApp : ChatApp).mount("#app");

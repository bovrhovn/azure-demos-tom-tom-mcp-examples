<script setup>
import { computed } from "vue";

const props = defineProps({
  name: { type: String, required: true },
  spin: { type: Boolean, default: false }
});

const ICONS = {
  "location-crosshairs": [
    { tag: "circle", attrs: { cx: 12, cy: 12, r: 7 } },
    { tag: "circle", attrs: { cx: 12, cy: 12, r: 2.5 } },
    { tag: "path", attrs: { d: "M12 2v3M12 19v3M2 12h3M19 12h3" } }
  ],
  "arrow-up-right-from-square": [
    { tag: "path", attrs: { d: "M14 4h6v6M20 4l-9 9" } },
    { tag: "path", attrs: { d: "M18 13v5a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h5" } }
  ],
  book: [
    { tag: "path", attrs: { d: "M4 5.5A2.5 2.5 0 0 1 6.5 3H20v17H6.5A2.5 2.5 0 0 0 4 22.5V5.5Z" } },
    { tag: "path", attrs: { d: "M4 19.5A2.5 2.5 0 0 1 6.5 17H20" } }
  ],
  sliders: [
    { tag: "path", attrs: { d: "M4 6h9M17 6h3M15 4v4M4 12h3M11 12h9M9 10v4M4 18h11M19 18h1M17 16v4" } }
  ],
  compass: [
    { tag: "circle", attrs: { cx: 12, cy: 12, r: 9 } },
    { tag: "polygon", attrs: { points: "15.5,8.5 13.5,13.5 8.5,15.5 10.5,10.5" } }
  ],
  plus: [
    { tag: "path", attrs: { d: "M12 5v14M5 12h14" } }
  ],
  comment: [
    { tag: "path", attrs: { d: "M20 14a2 2 0 0 1-2 2H8l-4 4V6a2 2 0 0 1 2-2h12a2 2 0 0 1 2 2v8Z" } }
  ],
  comments: [
    { tag: "path", attrs: { d: "M17 11a2 2 0 0 1-2 2H7l-4 3V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2v6Z" } },
    { tag: "path", attrs: { d: "M19 8h1a2 2 0 0 1 2 2v9l-3-2.5h-6a2 2 0 0 1-2-2" } }
  ],
  ellipsis: [
    { tag: "circle", attrs: { cx: 5, cy: 12, r: 1.5, fill: "currentColor", stroke: "none" } },
    { tag: "circle", attrs: { cx: 12, cy: 12, r: 1.5, fill: "currentColor", stroke: "none" } },
    { tag: "circle", attrs: { cx: 19, cy: 12, r: 1.5, fill: "currentColor", stroke: "none" } }
  ],
  "layer-group": [
    { tag: "polygon", attrs: { points: "12,3 21,8 12,13 3,8" } },
    { tag: "path", attrs: { d: "m3 12 9 5 9-5M3 16l9 5 9-5" } }
  ],
  pen: [
    { tag: "path", attrs: { d: "M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4 12.5-12.5Z" } }
  ],
  trash: [
    { tag: "path", attrs: { d: "M3 6h18M8 6V4a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1v2M6 6l1 14a2 2 0 0 0 2 2h6a2 2 0 0 0 2-2l1-14M10 11v6M14 11v6" } }
  ],
  "trash-can": [
    { tag: "path", attrs: { d: "M3 6h18M8 6V4a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1v2M6 6l1 14a2 2 0 0 0 2 2h6a2 2 0 0 0 2-2l1-14M10 11v6M14 11v6" } }
  ],
  xmark: [
    { tag: "path", attrs: { d: "M6 6l12 12M18 6 6 18" } }
  ],
  check: [
    { tag: "path", attrs: { d: "m5 12.5 4.5 4.5L19 7.5" } }
  ],
  "circle-check": [
    { tag: "circle", attrs: { cx: 12, cy: 12, r: 9 } },
    { tag: "path", attrs: { d: "m8.5 12.5 2.5 2.5 4.5-5" } }
  ],
  copy: [
    { tag: "rect", attrs: { x: 9, y: 9, width: 11, height: 11, rx: 2 } },
    { tag: "path", attrs: { d: "M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1" } }
  ],
  "circle-exclamation": [
    { tag: "circle", attrs: { cx: 12, cy: 12, r: 9 } },
    { tag: "path", attrs: { d: "M12 8v4.5M12 16h.01" } }
  ],
  "triangle-exclamation": [
    { tag: "path", attrs: { d: "M10.3 3.9 2.2 18a2 2 0 0 0 1.7 3h16.2a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0Z" } },
    { tag: "path", attrs: { d: "M12 9v4.5M12 17h.01" } }
  ],
  "plug-circle-xmark": [
    { tag: "path", attrs: { d: "M8 3v4M14 3v4M6 7h10v3a5 5 0 0 1-5 5v5" } },
    { tag: "circle", attrs: { cx: 18, cy: 18, r: 4 } },
    { tag: "path", attrs: { d: "m16.7 16.7 2.6 2.6M19.3 16.7l-2.6 2.6" } }
  ],
  "rotate-right": [
    { tag: "path", attrs: { d: "M21 4v6h-6" } },
    { tag: "path", attrs: { d: "M20.5 14a8.5 8.5 0 1 1-2.1-8.4L21 10" } }
  ],
  wrench: [
    { tag: "path", attrs: { d: "M14.7 6.3a4.5 4.5 0 0 0-5.8 5.8L3.5 17.5a2.1 2.1 0 0 0 3 3l5.4-5.4a4.5 4.5 0 0 0 5.8-5.8l-2.6 2.6-2.4-.6-.6-2.4 2.6-2.6Z" } }
  ],
  spinner: [
    { tag: "path", attrs: { d: "M21 12a9 9 0 1 1-3.2-6.9" } }
  ],
  "location-dot": [
    { tag: "path", attrs: { d: "M12 21s7-6.2 7-11.5a7 7 0 1 0-14 0C5 14.8 12 21 12 21Z" } },
    { tag: "circle", attrs: { cx: 12, cy: 9.5, r: 2.5 } }
  ],
  route: [
    { tag: "circle", attrs: { cx: 6, cy: 18, r: 2.5 } },
    { tag: "circle", attrs: { cx: 18, cy: 6, r: 2.5 } },
    { tag: "path", attrs: { d: "M8.5 18H13a3 3 0 0 0 0-6h-2a3 3 0 0 1 0-6h4.5" } }
  ],
  "charging-station": [
    { tag: "rect", attrs: { x: 4, y: 3, width: 11, height: 18, rx: 2 } },
    { tag: "path", attrs: { d: "M15 8h2a2 2 0 0 1 2 2v5a1.5 1.5 0 0 0 3 0v-4l-2-2M10 8l-2 4h3l-2 4" } }
  ],
  "traffic-light": [
    { tag: "rect", attrs: { x: 7, y: 2, width: 10, height: 20, rx: 3 } },
    { tag: "circle", attrs: { cx: 12, cy: 7, r: 1.5 } },
    { tag: "circle", attrs: { cx: 12, cy: 12, r: 1.5 } },
    { tag: "circle", attrs: { cx: 12, cy: 17, r: 1.5 } }
  ],
  user: [
    { tag: "circle", attrs: { cx: 12, cy: 8, r: 4 } },
    { tag: "path", attrs: { d: "M5 20a7 7 0 0 1 14 0" } }
  ],
  microchip: [
    { tag: "rect", attrs: { x: 6, y: 6, width: 12, height: 12, rx: 2 } },
    { tag: "rect", attrs: { x: 9.5, y: 9.5, width: 5, height: 5, rx: 1 } },
    { tag: "path", attrs: { d: "M9 2v4M15 2v4M9 18v4M15 18v4M2 9h4M2 15h4M18 9h4M18 15h4" } }
  ],
  "wand-magic-sparkles": [
    { tag: "path", attrs: { d: "m4 20 10.5-10.5M14.5 6.5l3 3M3 8l1-2.5L6.5 4.5 4 3.5 3 1 2 3.5-.5 4.5 2 5.5 3 8ZM19 4l.7-1.8L21.5 1.5 19.7.8 19-1l-.7 1.8L16.5 1.5l1.8.7L19 4Z" } },
    { tag: "path", attrs: { d: "m13.5 5.5 2-2a1.4 1.4 0 0 1 2 2l-2 2" } }
  ],
  "paper-plane": [
    { tag: "path", attrs: { d: "M21 3 10 14M21 3l-7 18-4-7-7-4 18-7Z" } }
  ],
  "arrow-up": [
    { tag: "path", attrs: { d: "M12 19V5M6 11l6-6 6 6" } }
  ],
  "arrow-right": [
    { tag: "path", attrs: { d: "M5 12h14M13 6l6 6-6 6" } }
  ],
  database: [
    { tag: "ellipse", attrs: { cx: 12, cy: 5.5, rx: 7.5, ry: 3 } },
    { tag: "path", attrs: { d: "M4.5 5.5v6.5c0 1.7 3.4 3 7.5 3s7.5-1.3 7.5-3V5.5" } },
    { tag: "path", attrs: { d: "M4.5 12v6.5c0 1.7 3.4 3 7.5 3s7.5-1.3 7.5-3V12" } }
  ],
  server: [
    { tag: "rect", attrs: { x: 3, y: 4, width: 18, height: 6, rx: 2 } },
    { tag: "rect", attrs: { x: 3, y: 14, width: 18, height: 6, rx: 2 } },
    { tag: "path", attrs: { d: "M7 7h.01M7 17h.01" } }
  ],
  "circle-info": [
    { tag: "circle", attrs: { cx: 12, cy: 12, r: 9 } },
    { tag: "path", attrs: { d: "M12 11v5M12 8h.01" } }
  ]
};

const shapes = computed(() => ICONS[props.name] ?? ICONS["circle-info"]);
const spinning = computed(() => props.spin || props.name === "spinner");
</script>

<template>
  <svg
    class="app-icon"
    :class="{ 'app-icon-spin': spinning }"
    :data-icon="name"
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    stroke-width="2"
    stroke-linecap="round"
    stroke-linejoin="round"
    aria-hidden="true"
    focusable="false"
  >
    <component :is="shape.tag" v-for="(shape, index) in shapes" :key="index" v-bind="shape.attrs" />
  </svg>
</template>

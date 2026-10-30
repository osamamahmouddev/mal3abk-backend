import type { Preview } from "@storybook/react";

const preview: Preview = {
  parameters: {
    actions: { argTypesRegex: "^on[A-Z].*" },
    controls: {
      matchers: {
        color: /(background|color)$/i,
        date: /Date$/i,
      },
    },
    a11y: {
      config: {
        rules: [
          {
            id: "color-contrast",
            enabled: true,
          },
          {
            id: "button-name",
            enabled: true,
          },
        ],
      },
    },
  },
  globalTypes: {
    direction: {
      name: "Direction",
      description: "Layout direction for RTL/LTR testing",
      defaultValue: "rtl",
      toolbar: {
        icon: "globe",
        items: [
          { value: "rtl", title: "RTL (Arabic)" },
          { value: "ltr", title: "LTR (English)" },
        ],
      },
    },
  },
};

export default preview;

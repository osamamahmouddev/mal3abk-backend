import type { Meta, StoryObj } from "@storybook/react";
import { Button } from "../components/Button";

const meta: Meta<typeof Button> = {
  title: "Components/Button",
  component: Button,
  tags: ["autodocs"],
  argTypes: {
    variant: {
      control: "select",
      options: ["primary", "secondary", "outline", "danger", "ghost"],
    },
    size: {
      control: "radio",
      options: ["sm", "md", "lg"],
    },
    isLoading: { control: "boolean" },
    disabled: { control: "boolean" },
  },
};

export default meta;
type Story = StoryObj<typeof Button>;

export const Primary: Story = {
  args: {
    children: "احجز الآن",
    variant: "primary",
    size: "md",
  },
};

export const AccentDanger: Story = {
  args: {
    children: "إلغاء الحجز",
    variant: "danger",
    size: "md",
  },
};

export const Loading: Story = {
  args: {
    children: "جاري الحفظ...",
    variant: "primary",
    isLoading: true,
  },
};

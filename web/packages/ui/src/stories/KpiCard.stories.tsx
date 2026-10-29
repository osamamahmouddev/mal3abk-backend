import type { Meta, StoryObj } from "@storybook/react";
import { KpiCard } from "../components/KpiCard";

const meta: Meta<typeof KpiCard> = {
  title: "Components/KpiCard",
  component: KpiCard,
  tags: ["autodocs"],
};

export default meta;
type Story = StoryObj<typeof KpiCard>;

export const RevenueKpi: Story = {
  args: {
    title: "إجمالي الإيرادات الشهرية",
    value: "٤٥,٢٠٠ ج.م",
    change: "+١٢.٥٪",
    trend: "up",
    trendLabel: "مقارنة بالشهر السابق",
  },
};

export const BookingsKpi: Story = {
  args: {
    title: "عدد الحجوزات النشطة",
    value: "١٢٨",
    change: "-٣.٢٪",
    trend: "down",
    trendLabel: "مقارنة بالأسبوع الماضي",
  },
};

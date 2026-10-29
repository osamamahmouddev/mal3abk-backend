import type { Meta, StoryObj } from "@storybook/react";
import { BookingStatusBadge, BookingStatus } from "../components/BookingStatusBadge";

const meta: Meta<typeof BookingStatusBadge> = {
  title: "Components/BookingStatusBadge",
  component: BookingStatusBadge,
  tags: ["autodocs"],
  argTypes: {
    status: {
      control: "select",
      options: [
        "Draft",
        "PendingDeposit",
        "ProofSubmitted",
        "UnderReview",
        "Confirmed",
        "Rejected",
        "Expired",
        "Cancelled",
        "Completed",
        "NoShow",
      ] as BookingStatus[],
    },
    locale: {
      control: "radio",
      options: ["ar", "en"],
    },
  },
};

export default meta;
type Story = StoryObj<typeof BookingStatusBadge>;

export const ConfirmedArabic: Story = {
  args: {
    status: "Confirmed",
    locale: "ar",
  },
};

export const ConfirmedEnglish: Story = {
  args: {
    status: "Confirmed",
    locale: "en",
  },
};

export const PendingDeposit: Story = {
  args: {
    status: "PendingDeposit",
    locale: "ar",
  },
};

export const ProofSubmitted: Story = {
  args: {
    status: "ProofSubmitted",
    locale: "ar",
  },
};

export const Rejected: Story = {
  args: {
    status: "Rejected",
    locale: "ar",
  },
};

# Malabak.Modules.Notifications

Bounded context orchestrating transactional push notifications, in-app alerts, WhatsApp messages, and SMS delivery.

## Key Features
- **Outbox Pattern:** Reliable asynchronous dispatch decoupling business events from external delivery networks.
- **Delivery Channels:**
  - Firebase Cloud Messaging (FCM) for iOS and Android player push alerts.
  - In-app notification inbox with read/unread tracking.
  - WhatsApp Business Cloud API & SMS for high-priority OTP and booking confirmation receipts.
- **PostgreSQL Schema:** `notifications.*`.

-- Seed Default System Admin into identity.users (Idempotent)
INSERT INTO identity.users (id, phone_number, email, full_name, password_hash, security_stamp, is_phone_number_confirmed, is_email_confirmed, two_factor_enabled, created_at_utc, roles)
VALUES (
    '00000000-0000-0000-0000-000000000001',
    '+201000000000',
    'admin@malabak.app',
    'Malabak System Administrator',
    'AQAAAAIAAYagAAAAEPwd+5...',
    '00000000000000000000000000000001',
    true,
    true,
    false,
    NOW() AT TIME ZONE 'UTC',
    '["Admin"]'
)
ON CONFLICT (id) DO NOTHING;

-- Seed Fictional Egyptian Demo Users
INSERT INTO identity.users (id, phone_number, email, full_name, security_stamp, is_phone_number_confirmed, is_email_confirmed, two_factor_enabled, created_at_utc, roles)
VALUES
    ('11111111-1111-1111-1111-111111111111', '+201012345678', NULL, 'عمرو زكي (Amr Zaki)', '11111111111111111111111111111111', true, false, false, NOW() AT TIME ZONE 'UTC', '["Player"]'),
    ('22222222-2222-2222-2222-222222222222', '+201098765432', NULL, 'محمد زيدان (Mohamed Zidan)', '22222222222222222222222222222222', true, false, false, NOW() AT TIME ZONE 'UTC', '["Player"]'),
    ('33333333-3333-3333-3333-333333333333', '+201100000001', 'owner.ahly@malabak.app', 'حسن شحاتة (Hassan Shehata)', '33333333333333333333333333333333', true, true, false, NOW() AT TIME ZONE 'UTC', '["VenueOwner"]')
ON CONFLICT (id) DO NOTHING;

-- Seed Fictional Egyptian Venue
INSERT INTO venues.venues (id, name, slug, description, owner_id, address, city, area, latitude, longitude, phone_number, is_approved, is_published, facilities, photos, created_at_utc)
VALUES (
    '44444444-4444-4444-4444-444444444444',
    'ملاعب النادي الأهلي - مدينة نصر',
    'al-ahly-club-nasr-city',
    'مجمع ملاعب خماسية وسباعية معتمدة بأحدث نجيل صناعي وكشافات ليلية',
    '33333333-3333-3333-3333-333333333333',
    'شارع أحمد فخري، المنطقة السادسة، مدينة نصر',
    'Cairo',
    'Nasr City',
    30.0561,
    31.3301,
    '+201000000001',
    true,
    true,
    '["Floodlights", "Parking", "Locker Rooms", "Cafeteria", "Shower", "Wi-Fi"]',
    '["https://assets.malabak.app/demo/ahly_main.jpg"]',
    NOW() AT TIME ZONE 'UTC'
)
ON CONFLICT (id) DO NOTHING;

-- Seed Pitches for Venue
INSERT INTO venues.pitches (id, venue_id, name, turf_type, capacity, length_meters, width_meters, is_active, photos, created_at_utc)
VALUES
    ('55555555-5555-5555-5555-555555555551', '44444444-4444-4444-4444-444444444444', 'ملعب التتش الصغير (خماسي)', 'Artificial 4G', 10, 40.0, 20.0, true, '["https://assets.malabak.app/demo/pitch_5a.jpg"]', NOW() AT TIME ZONE 'UTC'),
    ('55555555-5555-5555-5555-555555555552', '44444444-4444-4444-4444-444444444444', 'ملعب النجوم (سباعي)', 'Artificial 5G', 14, 55.0, 35.0, true, '["https://assets.malabak.app/demo/pitch_7a.jpg"]', NOW() AT TIME ZONE 'UTC')
ON CONFLICT (id) DO NOTHING;

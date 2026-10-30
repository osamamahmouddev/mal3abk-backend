-- ==============================================================================
-- Malabak Least-Privilege Database Role Provisioning
-- Enforces Principle of Least Privilege (PoLP) per SECURITY.md
-- ==============================================================================

-- 1. Create Roles if not existing
DO
$$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'malabak_migrator') THEN
        CREATE ROLE malabak_migrator WITH LOGIN ENCRYPTED PASSWORD '__MIGRATOR_PASSWORD__';
    ELSE
        ALTER ROLE malabak_migrator WITH LOGIN ENCRYPTED PASSWORD '__MIGRATOR_PASSWORD__';
    END IF;

    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'malabak_app') THEN
        CREATE ROLE malabak_app WITH LOGIN ENCRYPTED PASSWORD '__APP_PASSWORD__';
    ELSE
        ALTER ROLE malabak_app WITH LOGIN ENCRYPTED PASSWORD '__APP_PASSWORD__';
    END IF;

    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'malabak_readonly') THEN
        CREATE ROLE malabak_readonly WITH LOGIN ENCRYPTED PASSWORD '__READONLY_PASSWORD__';
    ELSE
        ALTER ROLE malabak_readonly WITH LOGIN ENCRYPTED PASSWORD '__READONLY_PASSWORD__';
    END IF;
END
$$;

-- Connect to target database
\c malabak;

-- Create mandatory extension
CREATE EXTENSION IF NOT EXISTS btree_gist;
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- 2. Schemas definition
CREATE SCHEMA IF NOT EXISTS identity;
CREATE SCHEMA IF NOT EXISTS venues;
CREATE SCHEMA IF NOT EXISTS booking;
CREATE SCHEMA IF NOT EXISTS payments;
CREATE SCHEMA IF NOT EXISTS notifications;
CREATE SCHEMA IF NOT EXISTS audit;
CREATE SCHEMA IF NOT EXISTS subscriptions;
CREATE SCHEMA IF NOT EXISTS teams;
CREATE SCHEMA IF NOT EXISTS matches;
CREATE SCHEMA IF NOT EXISTS support;

-- Assign schema ownership to migrator role for DDL operations
ALTER SCHEMA identity OWNER TO malabak_migrator;
ALTER SCHEMA venues OWNER TO malabak_migrator;
ALTER SCHEMA booking OWNER TO malabak_migrator;
ALTER SCHEMA payments OWNER TO malabak_migrator;
ALTER SCHEMA notifications OWNER TO malabak_migrator;
ALTER SCHEMA audit OWNER TO malabak_migrator;
ALTER SCHEMA subscriptions OWNER TO malabak_migrator;
ALTER SCHEMA teams OWNER TO malabak_migrator;
ALTER SCHEMA matches OWNER TO malabak_migrator;
ALTER SCHEMA support OWNER TO malabak_migrator;

-- 3. Grant schema usage
GRANT USAGE, CREATE ON SCHEMA identity, venues, booking, payments, notifications, audit, subscriptions, teams, matches, support TO malabak_migrator;
GRANT USAGE ON SCHEMA identity, venues, booking, payments, notifications, audit, subscriptions, teams, matches, support TO malabak_app;
GRANT USAGE ON SCHEMA identity, venues, booking, payments, notifications, audit, subscriptions, teams, matches, support TO malabak_readonly;

-- 4. App Role: DML Only (SELECT, INSERT, UPDATE, DELETE). Cannot DROP or ALTER.
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA identity, venues, booking, payments, notifications, audit, subscriptions, teams, matches, support TO malabak_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA identity, venues, booking, payments, notifications, audit, subscriptions, teams, matches, support TO malabak_app;
GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA identity, venues, booking, payments, notifications, audit, subscriptions, teams, matches, support TO malabak_app;

-- Default privileges for future tables created by migrator
ALTER DEFAULT PRIVILEGES FOR ROLE malabak_migrator IN SCHEMA identity, venues, booking, payments, notifications, audit, subscriptions, teams, matches, support
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO malabak_app;

ALTER DEFAULT PRIVILEGES FOR ROLE malabak_migrator IN SCHEMA identity, venues, booking, payments, notifications, audit, subscriptions, teams, matches, support
    GRANT USAGE, SELECT ON SEQUENCES TO malabak_app;

ALTER DEFAULT PRIVILEGES FOR ROLE malabak_migrator IN SCHEMA identity, venues, booking, payments, notifications, audit, subscriptions, teams, matches, support
    GRANT EXECUTE ON ROUTINES TO malabak_app;

-- 5. Readonly Role: SELECT Only
GRANT SELECT ON ALL TABLES IN SCHEMA identity, venues, booking, payments, notifications, audit, subscriptions, teams, matches, support TO malabak_readonly;
ALTER DEFAULT PRIVILEGES FOR ROLE malabak_migrator IN SCHEMA identity, venues, booking, payments, notifications, audit, subscriptions, teams, matches, support
    GRANT SELECT ON TABLES TO malabak_readonly;

-- Prevent public access
REVOKE ALL ON SCHEMA public FROM PUBLIC;

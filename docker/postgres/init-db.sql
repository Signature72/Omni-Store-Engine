-- =============================================================================
-- OmniStore Engine: PostgreSQL Multi-Database Initialization Script
-- Architecture Principle: Database-per-Service Isolation
-- Creates independent logical databases for Auth, Order, and Inventory microservices
-- =============================================================================

CREATE DATABASE auth_db;
CREATE DATABASE order_db;
CREATE DATABASE inventory_db;

-- Connect and add uuid-ossp extension for high-performance GUID generation
\c auth_db
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

\c order_db
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

\c inventory_db
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

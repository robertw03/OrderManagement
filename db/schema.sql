CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;
CREATE TABLE "mProduct" (
    "uidProductUUID" uuid NOT NULL,
    "txtName" character varying(200) NOT NULL,
    "intStockQuantity" integer NOT NULL,
    "decPrice" numeric(18,2) NOT NULL,
    "bolActive" boolean NOT NULL DEFAULT TRUE,
    "bolDeleted" boolean NOT NULL DEFAULT FALSE,
    "dtInserted" timestamp with time zone NOT NULL DEFAULT (now()),
    "txtInsertedBy" character varying(100) NOT NULL DEFAULT '',
    "dtUpdated" timestamp with time zone,
    "txtUpdatedBy" character varying(100),
    CONSTRAINT "PK_mProduct" PRIMARY KEY ("uidProductUUID"),
    CONSTRAINT "CK_mProduct_decPrice" CHECK ("decPrice" >= 0),
    CONSTRAINT "CK_mProduct_intStockQuantity" CHECK ("intStockQuantity" >= 0)
);

CREATE TABLE "trIdempotencyKey" (
    "uidIdempotencyKeyUUID" uuid NOT NULL,
    "txtCustomerId" character varying(64) NOT NULL,
    "txtIdempotencyKey" character varying(100) NOT NULL,
    "txtRequestHash" character varying(64) NOT NULL,
    "uidOrderUUID" uuid NOT NULL,
    "bolActive" boolean NOT NULL DEFAULT TRUE,
    "bolDeleted" boolean NOT NULL DEFAULT FALSE,
    "dtInserted" timestamp with time zone NOT NULL DEFAULT (now()),
    "txtInsertedBy" character varying(100) NOT NULL DEFAULT '',
    "dtUpdated" timestamp with time zone,
    "txtUpdatedBy" character varying(100),
    CONSTRAINT "PK_trIdempotencyKey" PRIMARY KEY ("uidIdempotencyKeyUUID")
);

CREATE TABLE "trOrder" (
    "uidOrderUUID" uuid NOT NULL,
    "txtCustomerId" character varying(64) NOT NULL,
    "txtShippingAddress" character varying(500) NOT NULL,
    "txtStatus" character varying(20) NOT NULL,
    "decTotalAmount" numeric(18,2) NOT NULL,
    "intVersion" integer NOT NULL,
    "bolActive" boolean NOT NULL DEFAULT TRUE,
    "bolDeleted" boolean NOT NULL DEFAULT FALSE,
    "dtInserted" timestamp with time zone NOT NULL DEFAULT (now()),
    "txtInsertedBy" character varying(100) NOT NULL DEFAULT '',
    "dtUpdated" timestamp with time zone,
    "txtUpdatedBy" character varying(100),
    CONSTRAINT "PK_trOrder" PRIMARY KEY ("uidOrderUUID"),
    CONSTRAINT "CK_trOrder_decTotalAmount" CHECK ("decTotalAmount" >= 0),
    CONSTRAINT "CK_trOrder_txtStatus" CHECK ("txtStatus" IN ('Pending','Confirmed','Shipped','Delivered','Cancelled'))
);

CREATE TABLE "trOrderItem" (
    "uidOrderItemUUID" uuid NOT NULL,
    "uidOrderUUID" uuid NOT NULL,
    "uidProductUUID" uuid NOT NULL,
    "txtProductName" character varying(200) NOT NULL,
    "decUnitPrice" numeric(18,2) NOT NULL,
    "intQuantity" integer NOT NULL,
    "decLineTotal" numeric(18,2) NOT NULL,
    "bolActive" boolean NOT NULL DEFAULT TRUE,
    "bolDeleted" boolean NOT NULL DEFAULT FALSE,
    "dtInserted" timestamp with time zone NOT NULL DEFAULT (now()),
    "txtInsertedBy" character varying(100) NOT NULL DEFAULT '',
    "dtUpdated" timestamp with time zone,
    "txtUpdatedBy" character varying(100),
    CONSTRAINT "PK_trOrderItem" PRIMARY KEY ("uidOrderItemUUID"),
    CONSTRAINT "CK_trOrderItem_intQuantity" CHECK ("intQuantity" > 0),
    CONSTRAINT "FK_trOrderItem_mProduct" FOREIGN KEY ("uidProductUUID") REFERENCES "mProduct" ("uidProductUUID") ON DELETE RESTRICT,
    CONSTRAINT "FK_trOrderItem_trOrder" FOREIGN KEY ("uidOrderUUID") REFERENCES "trOrder" ("uidOrderUUID") ON DELETE CASCADE
);

INSERT INTO "mProduct" ("uidProductUUID", "dtInserted", "txtInsertedBy", "bolActive", "bolDeleted", "txtName", "decPrice", "intStockQuantity", "dtUpdated", "txtUpdatedBy")
VALUES ('a1000000-0000-4000-8000-000000000001', TIMESTAMPTZ '2026-10-22T00:00:00+00:00', 'seed', TRUE, FALSE, 'Product X', 100000.0, 15, NULL, NULL);
INSERT INTO "mProduct" ("uidProductUUID", "dtInserted", "txtInsertedBy", "bolActive", "bolDeleted", "txtName", "decPrice", "intStockQuantity", "dtUpdated", "txtUpdatedBy")
VALUES ('a1000000-0000-4000-8000-000000000002', TIMESTAMPTZ '2026-10-22T00:00:00+00:00', 'seed', TRUE, FALSE, 'Indomie Goreng (1 dus)', 115000.0, 500, NULL, NULL);
INSERT INTO "mProduct" ("uidProductUUID", "dtInserted", "txtInsertedBy", "bolActive", "bolDeleted", "txtName", "decPrice", "intStockQuantity", "dtUpdated", "txtUpdatedBy")
VALUES ('a1000000-0000-4000-8000-000000000003', TIMESTAMPTZ '2026-10-22T00:00:00+00:00', 'seed', TRUE, FALSE, 'Aqua 600ml (1 dus)', 52000.0, 300, NULL, NULL);
INSERT INTO "mProduct" ("uidProductUUID", "dtInserted", "txtInsertedBy", "bolActive", "bolDeleted", "txtName", "decPrice", "intStockQuantity", "dtUpdated", "txtUpdatedBy")
VALUES ('a1000000-0000-4000-8000-000000000004', TIMESTAMPTZ '2026-10-22T00:00:00+00:00', 'seed', TRUE, FALSE, 'Beras Premium 5kg', 78500.0, 120, NULL, NULL);
INSERT INTO "mProduct" ("uidProductUUID", "dtInserted", "txtInsertedBy", "bolActive", "bolDeleted", "txtName", "decPrice", "intStockQuantity", "dtUpdated", "txtUpdatedBy")
VALUES ('a1000000-0000-4000-8000-000000000005', TIMESTAMPTZ '2026-10-22T00:00:00+00:00', 'seed', TRUE, FALSE, 'Minyak Goreng 2L', 38000.0, 200, NULL, NULL);

CREATE INDEX "IX_trIdempotencyKey_dtInserted" ON "trIdempotencyKey" ("dtInserted");

CREATE UNIQUE INDEX "UX_trIdempotencyKey_txtCustomerId_txtIdempotencyKey" ON "trIdempotencyKey" ("txtCustomerId", "txtIdempotencyKey");

CREATE UNIQUE INDEX "UX_trIdempotencyKey_uidOrderUUID" ON "trIdempotencyKey" ("uidOrderUUID");

CREATE INDEX "IX_trOrder_dtInserted" ON "trOrder" ("dtInserted");

CREATE INDEX "IX_trOrder_txtCustomerId_dtInserted" ON "trOrder" ("txtCustomerId", "dtInserted");

CREATE INDEX "IX_trOrder_txtStatus_dtInserted" ON "trOrder" ("txtStatus", "dtInserted");

CREATE INDEX "IX_trOrderItem_uidProductUUID" ON "trOrderItem" ("uidProductUUID");

CREATE UNIQUE INDEX "UX_trOrderItem_uidOrderUUID_uidProductUUID" ON "trOrderItem" ("uidOrderUUID", "uidProductUUID");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261022034437_InitialCreate', '10.0.13');

COMMIT;


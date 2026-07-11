-- migrate:up
CREATE TABLE public."L2CacheTable" (
    "Id" varchar(449) NOT NULL, 
    "Value" bytea NOT NULL, 
    "ExpiresAtTime" timestamp with time zone NOT NULL, 
    "SlidingExpirationInSeconds" bigint NULL,
    "AbsoluteExpiration" timestamp with time zone NULL, 
    CONSTRAINT "PK_L2CacheTable" PRIMARY KEY ("Id")
);
CREATE INDEX "Index_ExpiresAtTime" ON public."L2CacheTable"("ExpiresAtTime");

-- migrate:down
DROP TABLE IF EXISTS public."L2CacheTable";

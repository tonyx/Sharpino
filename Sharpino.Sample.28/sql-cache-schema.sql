CREATE TABLE [dbo].[L2CacheTable](
    Id nvarchar(449) COLLATE SQL_Latin1_General_CP1_CS_AS NOT NULL, 
    Value varbinary(MAX) NOT NULL, 
    ExpiresAtTime datetimeoffset NOT NULL, 
    SlidingExpirationInSeconds bigint NULL,
    AbsoluteExpiration datetimeoffset NULL, 
    PRIMARY KEY (Id)
);
CREATE NONCLUSTERED INDEX Index_ExpiresAtTime ON [dbo].[L2CacheTable](ExpiresAtTime);

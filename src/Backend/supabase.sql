-- ============================================================
-- OmniConnect Supabase Setup Script
-- Run this in Supabase SQL Editor to initialize the database
-- ============================================================

-- Create tables
CREATE TABLE IF NOT EXISTS public."Users" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "Username" VARCHAR(50) NOT NULL UNIQUE,
    "Email" VARCHAR(255) NOT NULL UNIQUE,
    "PasswordHash" TEXT NOT NULL,
    "DisplayName" VARCHAR(100) NOT NULL,
    "AvatarUrl" TEXT,
    "Bio" VARCHAR(500),
    "IsEmailVerified" BOOLEAN DEFAULT FALSE,
    "CreatedAt" TIMESTAMPTZ DEFAULT NOW(),
    "LastSeenAt" TIMESTAMPTZ,
    "IsDeleted" BOOLEAN DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS public."Messages" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "SenderId" UUID NOT NULL REFERENCES "Users"("Id"),
    "ReceiverId" UUID REFERENCES "Users"("Id"),
    "CommunityId" UUID,
    "ChannelId" UUID,
    "Type" INTEGER DEFAULT 0,
    "Content" TEXT NOT NULL,
    "MediaUrl" TEXT,
    "FileSize" BIGINT,
    "FileName" TEXT,
    "Status" INTEGER DEFAULT 1,
    "ReplyToId" UUID,
    "SentAt" TIMESTAMPTZ DEFAULT NOW(),
    "EditedAt" TIMESTAMPTZ,
    "DeletedAt" TIMESTAMPTZ,
    "ClientMessageId" VARCHAR(64)
);

CREATE TABLE IF NOT EXISTS public."Communities" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "Name" VARCHAR(100) NOT NULL,
    "Description" VARCHAR(1000),
    "IconUrl" TEXT,
    "OwnerId" UUID NOT NULL REFERENCES "Users"("Id"),
    "IsPublic" BOOLEAN DEFAULT TRUE,
    "CreatedAt" TIMESTAMPTZ DEFAULT NOW(),
    "IsDeleted" BOOLEAN DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS public."Channels" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "CommunityId" UUID NOT NULL REFERENCES "Communities"("Id"),
    "Name" VARCHAR(100) NOT NULL,
    "Description" VARCHAR(500),
    "IsVoice" BOOLEAN DEFAULT FALSE,
    "SortOrder" INTEGER DEFAULT 0,
    "CreatedAt" TIMESTAMPTZ DEFAULT NOW(),
    "IsDeleted" BOOLEAN DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS public."Friendships" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "RequesterId" UUID NOT NULL REFERENCES "Users"("Id"),
    "AddresseeId" UUID NOT NULL REFERENCES "Users"("Id"),
    "IsAccepted" BOOLEAN DEFAULT FALSE,
    "CreatedAt" TIMESTAMPTZ DEFAULT NOW(),
    "AcceptedAt" TIMESTAMPTZ,
    UNIQUE("RequesterId", "AddresseeId")
);

CREATE TABLE IF NOT EXISTS public."CommunityMembers" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "CommunityId" UUID NOT NULL REFERENCES "Communities"("Id"),
    "UserId" UUID NOT NULL REFERENCES "Users"("Id"),
    "Role" VARCHAR(20) DEFAULT 'Member',
    "JoinedAt" TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE("CommunityId", "UserId")
);

-- New tables for remaining features
CREATE TABLE IF NOT EXISTS public."Calls" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "CallerId" UUID NOT NULL REFERENCES "Users"("Id"),
    "CalleeId" UUID NOT NULL REFERENCES "Users"("Id"),
    "Type" INTEGER DEFAULT 0, -- 0=Audio, 1=Video
    "Status" INTEGER DEFAULT 0, -- 0=Initiated, 1=Accepted, 2=Rejected, 3=Ended
    "StartedAt" TIMESTAMPTZ DEFAULT NOW(),
    "EndedAt" TIMESTAMPTZ,
    "Duration" INTEGER DEFAULT 0
);

CREATE TABLE IF NOT EXISTS public."Files" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "UploaderId" UUID NOT NULL REFERENCES "Users"("Id"),
    "FileName" TEXT NOT NULL,
    "FileSize" BIGINT NOT NULL,
    "MimeType" VARCHAR(100),
    "StoragePath" TEXT NOT NULL,
    "PublicUrl" TEXT,
    "UploadedAt" TIMESTAMPTZ DEFAULT NOW(),
    "IsDeleted" BOOLEAN DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS public."Devices" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "UserId" UUID REFERENCES "Users"("Id"),
    "DeviceId" VARCHAR(100) NOT NULL,
    "DeviceName" VARCHAR(200),
    "DeviceModel" VARCHAR(200),
    "Platform" VARCHAR(50),
    "PlatformVersion" VARCHAR(50),
    "Roles" INTEGER DEFAULT 1,
    "IpAddress" VARCHAR(45),
    "Port" INTEGER DEFAULT 0,
    "AvailableTransports" TEXT[] DEFAULT '{}',
    "LastSeen" TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE("UserId", "DeviceId")
);

CREATE TABLE IF NOT EXISTS public."SyncQueue" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "UserId" UUID NOT NULL REFERENCES "Users"("Id"),
    "EntityType" VARCHAR(50) NOT NULL,
    "EntityId" UUID,
    "Action" VARCHAR(20) NOT NULL,
    "Payload" JSONB,
    "Status" INTEGER DEFAULT 0,
    "CreatedAt" TIMESTAMPTZ DEFAULT NOW(),
    "SyncedAt" TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS public."AiConversations" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "UserId" UUID NOT NULL REFERENCES "Users"("Id"),
    "Provider" VARCHAR(100) DEFAULT 'OpenAI',
    "ModelName" VARCHAR(100),
    "Title" VARCHAR(200),
    "MessagesJson" JSONB DEFAULT '[]',
    "TokenCount" INTEGER DEFAULT 0,
    "CreatedAt" TIMESTAMPTZ DEFAULT NOW(),
    "UpdatedAt" TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS public."Notifications" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "UserId" UUID NOT NULL REFERENCES "Users"("Id"),
    "Type" VARCHAR(50) NOT NULL,
    "Title" TEXT NOT NULL,
    "Body" TEXT,
    "Data" JSONB,
    "IsRead" BOOLEAN DEFAULT FALSE,
    "CreatedAt" TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS public."Analytics" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "Event" VARCHAR(100) NOT NULL,
    "UserId" UUID REFERENCES "Users"("Id"),
    "Data" JSONB,
    "CreatedAt" TIMESTAMPTZ DEFAULT NOW()
);

-- Indexes
CREATE INDEX IF NOT EXISTS idx_messages_sender ON "Messages"("SenderId");
CREATE INDEX IF NOT EXISTS idx_messages_channel ON "Messages"("CommunityId", "ChannelId", "SentAt" DESC);
CREATE INDEX IF NOT EXISTS idx_messages_conversation ON "Messages"("SenderId", "ReceiverId");
CREATE INDEX IF NOT EXISTS idx_notifications_user ON "Notifications"("UserId", "IsRead");
CREATE INDEX IF NOT EXISTS idx_syncqueue_user ON "SyncQueue"("UserId", "Status");
CREATE INDEX IF NOT EXISTS idx_devices_user ON "Devices"("UserId");

-- Enable Row Level Security (optional - can be managed by backend)
ALTER TABLE public."Users" ENABLE ROW LEVEL SECURITY;
ALTER TABLE public."Messages" ENABLE ROW LEVEL SECURITY;
ALTER TABLE public."Communities" ENABLE ROW LEVEL SECURITY;

-- Supabase Realtime: Enable replication for real-time features
ALTER PUBLICATION supabase_realtime ADD TABLE public."Messages";
ALTER PUBLICATION supabase_realtime ADD TABLE public."Calls";
ALTER PUBLICATION supabase_realtime ADD TABLE public."Notifications";
-- Fix Userld -> UserId typo and set FK to public."Users" in Supabase.
-- Run this in Supabase Dashboard: SQL Editor -> New query -> paste -> Run.

-- ---------- SmileScans (rename only if column is "Userld") ----------
ALTER TABLE "SmileScans" DROP CONSTRAINT IF EXISTS "SmileScans_Userld_fkey";
ALTER TABLE "SmileScans" DROP CONSTRAINT IF EXISTS "SmileScans_UserId_fkey";
DO $$
BEGIN
  IF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_schema = 'public' AND table_name = 'SmileScans' AND column_name = 'Userld'
  ) THEN
    ALTER TABLE "SmileScans" RENAME COLUMN "Userld" TO "UserId";
  END IF;
END $$;
ALTER TABLE "SmileScans"
  ADD CONSTRAINT "SmileScans_UserId_fkey"
  FOREIGN KEY ("UserId") REFERENCES public."Users"("Id");

-- ---------- UserSettings (rename only if column is "Userld") ----------
ALTER TABLE "UserSettings" DROP CONSTRAINT IF EXISTS "UserSettings_Userld_fkey";
ALTER TABLE "UserSettings" DROP CONSTRAINT IF EXISTS "UserSettings_UserId_fkey";
DO $$
BEGIN
  IF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_schema = 'public' AND table_name = 'UserSettings' AND column_name = 'Userld'
  ) THEN
    ALTER TABLE "UserSettings" RENAME COLUMN "Userld" TO "UserId";
  END IF;
END $$;
ALTER TABLE "UserSettings"
  ADD CONSTRAINT "UserSettings_UserId_fkey"
  FOREIGN KEY ("UserId") REFERENCES public."Users"("Id");

-- ---------- AIUsageLogs (rename only if column is "Userld") ----------
ALTER TABLE "AIUsageLogs" DROP CONSTRAINT IF EXISTS "AIUsageLogs_Userld_fkey";
ALTER TABLE "AIUsageLogs" DROP CONSTRAINT IF EXISTS "AIUsageLogs_UserId_fkey";
DO $$
BEGIN
  IF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_schema = 'public' AND table_name = 'AIUsageLogs' AND column_name = 'Userld'
  ) THEN
    ALTER TABLE "AIUsageLogs" RENAME COLUMN "Userld" TO "UserId";
  END IF;
END $$;
ALTER TABLE "AIUsageLogs"
  ADD CONSTRAINT "AIUsageLogs_UserId_fkey"
  FOREIGN KEY ("UserId") REFERENCES public."Users"("Id");

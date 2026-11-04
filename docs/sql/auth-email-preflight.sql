-- Read-only CP1 preflight. Works before either authentication migration.
-- Return aggregate counts only; never put account/email mapping in shared logs.
WITH candidates AS (
    SELECT "Email", "PasswordHash", "Status",
        btrim("Email", E' \t\n\r\013\f') AS trimmed,
        CASE WHEN length(btrim("Email", E' \t\n\r\013\f')) <= 254
            AND length(split_part(btrim("Email", E' \t\n\r\013\f'), '@', 1)) <= 64
            AND btrim("Email", E' \t\n\r\013\f') COLLATE "C" ~ '^[A-Za-z0-9!#$%&''*+/=?^_`{|}~-]+(?:\.[A-Za-z0-9!#$%&''*+/=?^_`{|}~-]+)*@[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)+$'
        THEN upper(btrim("Email", E' \t\n\r\013\f') COLLATE "C") ELSE NULL END AS normalized
    FROM "TAIKHOAN"
), collisions AS (
    SELECT count(*) AS accounts FROM candidates
    WHERE normalized IS NOT NULL GROUP BY normalized HAVING count(*) > 1
)
SELECT count(*) AS total_accounts,
    count(*) FILTER (WHERE trimmed IS NULL OR trimmed = '') AS missing_email,
    count(*) FILTER (WHERE trimmed <> '' AND normalized IS NULL) AS unsupported_email,
    count(*) FILTER (WHERE normalized IS NOT NULL) AS supported_email,
    count(*) FILTER (WHERE "PasswordHash" IS NULL OR btrim("PasswordHash") = '') AS missing_password,
    count(*) FILTER (WHERE "Status" IN ('Locked', 'Deactivated')) AS administratively_disabled,
    (SELECT count(*) FROM collisions) AS collision_groups,
    (SELECT coalesce(sum(accounts), 0) FROM collisions) AS accounts_in_collisions
FROM candidates;

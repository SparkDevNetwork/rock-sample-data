SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

-- =============================================
-- Description: Sample data calibration, part 5: prayer in detail.
--
--              Part 3 measures prayer volume and a few flags. This part
--              measures how a prayer team uses Rock: who asks, how fast
--              requests are approved, how often people pray for them, how
--              the praying is spread across the team, and how comments
--              and answers are used.
--
--              Every per-request measure stands alone. Run part 2 on the
--              same database only if you also want its yearly volumes,
--              which are scaled by part 2's active adult count.
--
--              Collects anonymous, aggregate statistics from a live Rock
--              database. Returns no names, no Ids, no category names, and
--              no request text.
--
--              HOW TO RUN IN ROCK:
--              1. Open Admin Tools > Settings > Power Tools > SQL Command.
--              2. Leave "Selection Query?" set to Yes. Rock runs the script in
--                 a transaction and rolls it back, so nothing can change.
--              3. Paste this whole file and click Run.
--              4. Rock shows a yellow warning that the query "modifies data."
--                 That is expected. The script only uses temp tables, and Rock
--                 rolled them back.
--              5. Export the grid to Excel, or copy the rows. Send the result.
--
--              Use the same @ChurchLabel as in part 2.
--              Also runs in SSMS. Requires SQL Server 2016 or later.
-- Date: 2026-10-19
-- =============================================

-- Settings
DECLARE @ChurchLabel NVARCHAR(50) = 'Unlabeled'; -- Use a neutral label like source-2, never a church name.

-- The window is the last 24 complete months. The current month is excluded.
DECLARE @EndDate DATE = DATEFROMPARTS( YEAR( GETDATE() ), MONTH( GETDATE() ), 1 );
DECLARE @Start24 DATE = DATEADD( MONTH, -24, @EndDate );

-- Lookups
DECLARE @PrayerChannelId INT = ( SELECT TOP 1 [Id] FROM [InteractionChannel] WHERE [Guid] = '3D49FB99-94D1-4F63-B1A2-30D4FEDE11E9' );
DECLARE @PrayerCommentNoteTypeId INT = ( SELECT TOP 1 [Id] FROM [NoteType] WHERE [Guid] = '0EBABD75-0890-4756-A9EE-62626282BB5D' );

-- Clean up temp tables from an earlier run in the same SSMS session.
DROP TABLE IF EXISTS #Req, #Component, #Prayed, #ReqPrayed, #Comment;

-- =============================================
-- Base sets
-- =============================================

-- Requests entered in the window, with the requester's age and gender at entry.
SELECT
    [pr].[Id] AS [PrayerRequestId],
    [pr].[EnteredDateTime],
    [pr].[ExpirationDate],
    [pr].[ApprovedOnDateTime],
    [pr].[ApprovedByPersonAliasId],
    [pr].[CategoryId],
    [pr].[GroupId],
    [pr].[CampusId],
    [pr].[IsApproved],
    [pr].[IsPublic],
    [pr].[IsUrgent],
    [pr].[IsActive],
    [pr].[AllowComments],
    [pr].[RequestedByPersonAliasId],
    ISNULL( [pr].[PrayerCount], 0 ) AS [PrayerCount],
    ISNULL( [pr].[FlagCount], 0 ) AS [FlagCount],
    LEN( [pr].[Text] ) AS [TextLength],
    LEN( [pr].[Answer] ) AS [AnswerLength],
    [pa].[PersonId] AS [RequesterPersonId],
    [p].[Gender] AS [RequesterGender],
    CASE WHEN [p].[BirthDate] IS NULL THEN NULL ELSE DATEDIFF( DAY, [p].[BirthDate], [pr].[EnteredDateTime] ) / 365.25 END AS [RequesterAge]
INTO #Req
FROM [PrayerRequest] AS [pr]
LEFT JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [pr].[RequestedByPersonAliasId]
LEFT JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
WHERE [pr].[EnteredDateTime] >= @Start24 AND [pr].[EnteredDateTime] < @EndDate;

CREATE CLUSTERED INDEX [IX_PrayerRequestId] ON #Req ( [PrayerRequestId] );

-- Rock writes one interaction component per prayer request in the Prayer Events channel.
SELECT [ic].[Id] AS [ComponentId], [ic].[EntityId] AS [PrayerRequestId]
INTO #Component
FROM [InteractionComponent] AS [ic]
WHERE [ic].[InteractionChannelId] = @PrayerChannelId
    AND [ic].[EntityId] IS NOT NULL;

/*
    10/19/26 - CLAUDE

    The Interaction table is often one of the largest in the database. This
    reads it only through the prayer components, so SQL seeks on the
    component index instead of scanning every interaction.

    Reason: Keep the script fast on large churches.
*/
SELECT
    [c].[PrayerRequestId],
    [i].[InteractionDateTime],
    [pa].[PersonId] AS [PrayingPersonId]
INTO #Prayed
FROM #Component AS [c]
INNER JOIN [Interaction] AS [i] ON [i].[InteractionComponentId] = [c].[ComponentId]
LEFT JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [i].[PersonAliasId]
WHERE [i].[Operation] = 'Prayed'
    AND [i].[InteractionDateTime] >= @Start24
    AND [i].[InteractionDateTime] < @EndDate;

-- Prayers per request, for requests entered in the window.
SELECT
    [r].[PrayerRequestId],
    [r].[EnteredDateTime],
    [r].[PrayerCount],
    COUNT( [pd].[PrayerRequestId] ) AS [Prayers],
    MIN( [pd].[InteractionDateTime] ) AS [FirstPrayerDateTime],
    MAX( [pd].[InteractionDateTime] ) AS [LastPrayerDateTime]
INTO #ReqPrayed
FROM #Req AS [r]
LEFT JOIN #Prayed AS [pd] ON [pd].[PrayerRequestId] = [r].[PrayerRequestId]
GROUP BY [r].[PrayerRequestId], [r].[EnteredDateTime], [r].[PrayerCount];

-- Prayer comments are notes on the request.
SELECT
    [n].[EntityId] AS [PrayerRequestId],
    [n].[CreatedDateTime],
    [n].[ParentNoteId],
    LEN( [n].[Text] ) AS [TextLength],
    [pa].[PersonId] AS [AuthorPersonId]
INTO #Comment
FROM #Req AS [r]
INNER JOIN [Note] AS [n] ON [n].[EntityId] = [r].[PrayerRequestId] AND [n].[NoteTypeId] = @PrayerCommentNoteTypeId
LEFT JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [n].[CreatedByPersonAliasId];

-- =============================================
-- Summary and months (the same shape as part 3, so this part can stand alone)
-- =============================================

DECLARE @JPrayerSummary NVARCHAR(MAX) = (
    SELECT
        COUNT( * ) AS [requests],
        CAST( AVG( CASE WHEN [r].[IsApproved] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctApproved],
        CAST( AVG( CASE WHEN [r].[IsPublic] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctPublic],
        CAST( AVG( CASE WHEN [r].[IsUrgent] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctUrgent],
        CAST( AVG( CASE WHEN [r].[AllowComments] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctAllowComments],
        CAST( AVG( CASE WHEN ISNULL( [r].[AnswerLength], 0 ) > 0 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctAnswered],
        CAST( AVG( CASE WHEN [r].[RequestedByPersonAliasId] IS NOT NULL THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctLinkedToPerson],
        CAST( AVG( CASE WHEN [r].[CategoryId] IS NOT NULL THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctWithCategory],
        CAST( AVG( [r].[PrayerCount] * 1.0 ) AS DECIMAL(6,1) ) AS [avgPrayerCount],
        CAST( AVG( [r].[TextLength] * 1.0 ) AS DECIMAL(7,0) ) AS [avgTextLength]
    FROM #Req AS [r]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

DECLARE @JPrayerByMonth NVARCHAR(MAX) = (
    SELECT
        CONVERT( CHAR(7), [r].[EnteredDateTime], 120 ) AS [month],
        COUNT( * ) AS [requests]
    FROM #Req AS [r]
    GROUP BY CONVERT( CHAR(7), [r].[EnteredDateTime], 120 )
    ORDER BY CONVERT( CHAR(7), [r].[EnteredDateTime], 120 )
    FOR JSON PATH
);

-- =============================================
-- Request details: links, flags, expiration, approval, text, and prayer counts
-- =============================================

DECLARE @JPrayerDetail NVARCHAR(MAX) = (
    SELECT [s].*, [ex].*, [ap].*, [tx].*, [an].*, [pc].*
    FROM (
        SELECT
            CAST( AVG( CASE WHEN [r].[GroupId] IS NOT NULL THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctWithGroup],
            CAST( AVG( CASE WHEN [r].[CampusId] IS NOT NULL THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctWithCampus],
            CAST( AVG( CASE WHEN [r].[IsActive] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctActive],
            CAST( AVG( CASE WHEN [r].[FlagCount] > 0 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctFlagged],
            CAST( AVG( CASE WHEN [r].[ExpirationDate] IS NOT NULL THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctWithExpiration],
            CAST( AVG( CASE WHEN [r].[ApprovedByPersonAliasId] IS NOT NULL THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctApprovedByAPerson],
            CAST( AVG( CASE WHEN CAST( [r].[EnteredDateTime] AS TIME ) = '00:00' THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctEnteredAtMidnight],
            CAST( AVG( CASE WHEN [r].[PrayerCount] = 0 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctPrayerCount0],
            CAST( AVG( CASE WHEN [r].[PrayerCount] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctPrayerCount1],
            CAST( AVG( CASE WHEN [r].[PrayerCount] BETWEEN 2 AND 4 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctPrayerCount2To4],
            CAST( AVG( CASE WHEN [r].[PrayerCount] BETWEEN 5 AND 9 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctPrayerCount5To9],
            CAST( AVG( CASE WHEN [r].[PrayerCount] BETWEEN 10 AND 19 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctPrayerCount10To19],
            CAST( AVG( CASE WHEN [r].[PrayerCount] >= 20 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctPrayerCount20Plus]
        FROM #Req AS [r]
    ) AS [s]
    OUTER APPLY (
        SELECT TOP 1
            CAST( PERCENTILE_CONT( 0.50 ) WITHIN GROUP ( ORDER BY DATEDIFF( DAY, [r].[EnteredDateTime], [r].[ExpirationDate] ) ) OVER () AS DECIMAL(7,1) ) AS [daysToExpireP50],
            CAST( PERCENTILE_CONT( 0.90 ) WITHIN GROUP ( ORDER BY DATEDIFF( DAY, [r].[EnteredDateTime], [r].[ExpirationDate] ) ) OVER () AS DECIMAL(7,1) ) AS [daysToExpireP90]
        FROM #Req AS [r]
        WHERE [r].[ExpirationDate] IS NOT NULL
    ) AS [ex]
    OUTER APPLY (
        SELECT TOP 1
            CAST( PERCENTILE_CONT( 0.50 ) WITHIN GROUP ( ORDER BY DATEDIFF( MINUTE, [r].[EnteredDateTime], [r].[ApprovedOnDateTime] ) / 60.0 ) OVER () AS DECIMAL(9,1) ) AS [hoursToApproveP50],
            CAST( PERCENTILE_CONT( 0.90 ) WITHIN GROUP ( ORDER BY DATEDIFF( MINUTE, [r].[EnteredDateTime], [r].[ApprovedOnDateTime] ) / 60.0 ) OVER () AS DECIMAL(9,1) ) AS [hoursToApproveP90]
        FROM #Req AS [r]
        WHERE [r].[ApprovedOnDateTime] >= [r].[EnteredDateTime]
    ) AS [ap]
    OUTER APPLY (
        SELECT TOP 1
            CAST( PERCENTILE_CONT( 0.10 ) WITHIN GROUP ( ORDER BY [r].[TextLength] ) OVER () AS DECIMAL(7,0) ) AS [textLengthP10],
            CAST( PERCENTILE_CONT( 0.50 ) WITHIN GROUP ( ORDER BY [r].[TextLength] ) OVER () AS DECIMAL(7,0) ) AS [textLengthP50],
            CAST( PERCENTILE_CONT( 0.90 ) WITHIN GROUP ( ORDER BY [r].[TextLength] ) OVER () AS DECIMAL(7,0) ) AS [textLengthP90]
        FROM #Req AS [r]
        WHERE [r].[TextLength] > 0
    ) AS [tx]
    OUTER APPLY (
        SELECT TOP 1
            CAST( PERCENTILE_CONT( 0.50 ) WITHIN GROUP ( ORDER BY [r].[AnswerLength] ) OVER () AS DECIMAL(7,0) ) AS [answerLengthP50],
            CAST( PERCENTILE_CONT( 0.50 ) WITHIN GROUP ( ORDER BY DATEDIFF( DAY, [r].[EnteredDateTime], @EndDate ) ) OVER () AS DECIMAL(7,0) ) AS [answeredRequestAgeDaysP50]
        FROM #Req AS [r]
        WHERE [r].[AnswerLength] > 0
    ) AS [an]
    OUTER APPLY (
        SELECT TOP 1
            CAST( PERCENTILE_CONT( 0.50 ) WITHIN GROUP ( ORDER BY [r].[PrayerCount] ) OVER () AS DECIMAL(7,1) ) AS [prayerCountP50],
            CAST( PERCENTILE_CONT( 0.90 ) WITHIN GROUP ( ORDER BY [r].[PrayerCount] ) OVER () AS DECIMAL(7,1) ) AS [prayerCountP90]
        FROM #Req AS [r]
    ) AS [pc]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

-- =============================================
-- When requests come in (day of week and time of day)
-- =============================================

-- 1900-01-01 was a Monday, so this weekday math does not depend on DATEFIRST or the server language.
DECLARE @JPrayerByWeekday NVARCHAR(MAX) = (
    SELECT
        CHOOSE( [w].[DayNumber] + 1, 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun' ) AS [weekday],
        CAST( COUNT( * ) * 100.0 / NULLIF( ( SELECT COUNT( * ) FROM #Req ), 0 ) AS DECIMAL(5,1) ) AS [pctRequests]
    FROM (
        SELECT DATEDIFF( DAY, '19000101', CAST( [r].[EnteredDateTime] AS DATE ) ) % 7 AS [DayNumber]
        FROM #Req AS [r]
    ) AS [w]
    GROUP BY [w].[DayNumber]
    ORDER BY [w].[DayNumber]
    FOR JSON PATH
);

DECLARE @JPrayerByHourBand NVARCHAR(MAX) = (
    SELECT
        [h].[HourBand] AS [hourBand],
        CAST( COUNT( * ) * 100.0 / NULLIF( ( SELECT COUNT( * ) FROM #Req ), 0 ) AS DECIMAL(5,1) ) AS [pctRequests]
    FROM (
        SELECT
            CASE
                WHEN DATEPART( HOUR, [r].[EnteredDateTime] ) < 6 THEN '00-05'
                WHEN DATEPART( HOUR, [r].[EnteredDateTime] ) < 12 THEN '06-11'
                WHEN DATEPART( HOUR, [r].[EnteredDateTime] ) < 18 THEN '12-17'
                ELSE '18-23'
            END AS [HourBand]
        FROM #Req AS [r]
    ) AS [h]
    GROUP BY [h].[HourBand]
    ORDER BY [h].[HourBand]
    FOR JSON PATH
);

-- =============================================
-- Categories (shape only; category names are never returned)
-- =============================================

DECLARE @JPrayerCategories NVARCHAR(MAX) = (
    SELECT
        COUNT( * ) AS [categoriesUsed],
        CAST( SUM( CASE WHEN [c].[Rank] = 1 THEN [c].[Requests] ELSE 0 END ) * 100.0 / NULLIF( SUM( [c].[Requests] ), 0 ) AS DECIMAL(5,1) ) AS [pctInTopCategory],
        CAST( SUM( CASE WHEN [c].[Rank] <= 3 THEN [c].[Requests] ELSE 0 END ) * 100.0 / NULLIF( SUM( [c].[Requests] ), 0 ) AS DECIMAL(5,1) ) AS [pctInTop3Categories],
        CAST( SUM( CASE WHEN [c].[Rank] <= 5 THEN [c].[Requests] ELSE 0 END ) * 100.0 / NULLIF( SUM( [c].[Requests] ), 0 ) AS DECIMAL(5,1) ) AS [pctInTop5Categories],
        CAST( AVG( CASE WHEN [cat].[ParentCategoryId] IS NOT NULL THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctOfUsedCategoriesWithAParent]
    FROM (
        SELECT
            [r].[CategoryId],
            COUNT( * ) AS [Requests],
            ROW_NUMBER() OVER ( ORDER BY COUNT( * ) DESC ) AS [Rank]
        FROM #Req AS [r]
        WHERE [r].[CategoryId] IS NOT NULL
        GROUP BY [r].[CategoryId]
    ) AS [c]
    LEFT JOIN [Category] AS [cat] ON [cat].[Id] = [c].[CategoryId]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

-- =============================================
-- Who asks
-- =============================================

DECLARE @JPrayerRequesters NVARCHAR(MAX) = (
    SELECT [s].*, [rp].*
    FROM (
        SELECT
            COUNT( DISTINCT [r].[RequesterPersonId] ) AS [requesters],
            CAST( AVG( CASE WHEN [r].[RequesterAge] < 18 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctAgeUnder18],
            CAST( AVG( CASE WHEN [r].[RequesterAge] >= 18 AND [r].[RequesterAge] < 30 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctAge18To29],
            CAST( AVG( CASE WHEN [r].[RequesterAge] >= 30 AND [r].[RequesterAge] < 45 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctAge30To44],
            CAST( AVG( CASE WHEN [r].[RequesterAge] >= 45 AND [r].[RequesterAge] < 65 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctAge45To64],
            CAST( AVG( CASE WHEN [r].[RequesterAge] >= 65 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctAge65Plus],
            CAST( AVG( CASE WHEN [r].[RequesterAge] IS NULL THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctAgeUnknown],
            CAST( AVG( CASE WHEN [r].[RequesterGender] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctMale],
            CAST( AVG( CASE WHEN [r].[RequesterGender] = 2 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctFemale],
            CAST( AVG( CASE WHEN ISNULL( [r].[RequesterGender], 0 ) = 0 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctGenderUnknown]
        FROM #Req AS [r]
        WHERE [r].[RequesterPersonId] IS NOT NULL
    ) AS [s]
    OUTER APPLY (
        SELECT
            CAST( AVG( CASE WHEN [q].[Requests] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctRequestersWith1Request],
            CAST( AVG( CASE WHEN [q].[Requests] BETWEEN 2 AND 3 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctRequestersWith2To3Requests],
            CAST( AVG( CASE WHEN [q].[Requests] >= 4 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctRequestersWith4PlusRequests],
            CAST( AVG( CASE WHEN [pp].[PrayingPersonId] IS NOT NULL THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctRequestersWhoAlsoPrayed]
        FROM (
            SELECT [r].[RequesterPersonId], COUNT( * ) AS [Requests]
            FROM #Req AS [r]
            WHERE [r].[RequesterPersonId] IS NOT NULL
            GROUP BY [r].[RequesterPersonId]
        ) AS [q]
        LEFT JOIN (
            SELECT DISTINCT [pd].[PrayingPersonId]
            FROM #Prayed AS [pd]
        ) AS [pp] ON [pp].[PrayingPersonId] = [q].[RequesterPersonId]
    ) AS [rp]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

-- =============================================
-- Who prays, and how often requests are prayed for
-- =============================================

DECLARE @JPrayerTeam NVARCHAR(MAX) = (
    SELECT [s].*, [pp].*, [cn].*, [rq].*, [fp].*
    FROM (
        SELECT
            COUNT( * ) AS [prayers],
            COUNT( DISTINCT [pd].[PrayingPersonId] ) AS [prayingPeople],
            CAST( AVG( CASE WHEN [pd].[PrayingPersonId] IS NOT NULL THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctPrayersWithAPerson],
            ( SELECT COUNT( * ) FROM #Component ) AS [prayerComponents]
        FROM #Prayed AS [pd]
    ) AS [s]
    OUTER APPLY (
        SELECT TOP 1
            CAST( PERCENTILE_CONT( 0.50 ) WITHIN GROUP ( ORDER BY [x].[Prayers] ) OVER () AS DECIMAL(7,1) ) AS [prayersPerPrayingPersonP50],
            CAST( PERCENTILE_CONT( 0.90 ) WITHIN GROUP ( ORDER BY [x].[Prayers] ) OVER () AS DECIMAL(7,1) ) AS [prayersPerPrayingPersonP90]
        FROM (
            SELECT [pd].[PrayingPersonId], COUNT( * ) AS [Prayers]
            FROM #Prayed AS [pd]
            WHERE [pd].[PrayingPersonId] IS NOT NULL
            GROUP BY [pd].[PrayingPersonId]
        ) AS [x]
    ) AS [pp]
    OUTER APPLY (
        -- Share of all prayers made by the busiest tenth of the people who pray.
        SELECT
            CAST( SUM( CASE WHEN [x].[Tenth] = 1 THEN [x].[Prayers] ELSE 0 END ) * 100.0 / NULLIF( SUM( [x].[Prayers] ), 0 ) AS DECIMAL(5,1) ) AS [pctPrayersByTop10PctOfPrayingPeople]
        FROM (
            SELECT COUNT( * ) AS [Prayers], NTILE( 10 ) OVER ( ORDER BY COUNT( * ) DESC ) AS [Tenth]
            FROM #Prayed AS [pd]
            WHERE [pd].[PrayingPersonId] IS NOT NULL
            GROUP BY [pd].[PrayingPersonId]
        ) AS [x]
    ) AS [cn]
    OUTER APPLY (
        SELECT
            CAST( AVG( CASE WHEN [rp].[Prayers] > 0 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctRequestsPrayedFor],
            CAST( AVG( [rp].[Prayers] * 1.0 ) AS DECIMAL(7,2) ) AS [avgPrayedInteractionsPerRequest],
            CAST( AVG( [rp].[PrayerCount] * 1.0 ) AS DECIMAL(7,2) ) AS [avgPrayerCountPerRequest]
        FROM #ReqPrayed AS [rp]
    ) AS [rq]
    OUTER APPLY (
        SELECT TOP 1
            CAST( PERCENTILE_CONT( 0.50 ) WITHIN GROUP ( ORDER BY [rp].[Prayers] ) OVER () AS DECIMAL(7,1) ) AS [prayersPerPrayedRequestP50],
            CAST( PERCENTILE_CONT( 0.90 ) WITHIN GROUP ( ORDER BY [rp].[Prayers] ) OVER () AS DECIMAL(7,1) ) AS [prayersPerPrayedRequestP90],
            CAST( PERCENTILE_CONT( 0.50 ) WITHIN GROUP ( ORDER BY DATEDIFF( MINUTE, [rp].[EnteredDateTime], [rp].[FirstPrayerDateTime] ) / 60.0 ) OVER () AS DECIMAL(9,1) ) AS [hoursToFirstPrayerP50],
            CAST( PERCENTILE_CONT( 0.90 ) WITHIN GROUP ( ORDER BY DATEDIFF( MINUTE, [rp].[EnteredDateTime], [rp].[FirstPrayerDateTime] ) / 60.0 ) OVER () AS DECIMAL(9,1) ) AS [hoursToFirstPrayerP90],
            CAST( PERCENTILE_CONT( 0.50 ) WITHIN GROUP ( ORDER BY DATEDIFF( DAY, [rp].[EnteredDateTime], [rp].[LastPrayerDateTime] ) ) OVER () AS DECIMAL(7,1) ) AS [daysToLastPrayerP50],
            CAST( PERCENTILE_CONT( 0.90 ) WITHIN GROUP ( ORDER BY DATEDIFF( DAY, [rp].[EnteredDateTime], [rp].[LastPrayerDateTime] ) ) OVER () AS DECIMAL(7,1) ) AS [daysToLastPrayerP90]
        FROM #ReqPrayed AS [rp]
        WHERE [rp].[Prayers] > 0
    ) AS [fp]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

-- =============================================
-- Comments
-- =============================================

DECLARE @JPrayerComments NVARCHAR(MAX) = (
    SELECT [s].*, [cr].*, [ct].*
    FROM (
        SELECT
            COUNT( * ) AS [comments],
            CAST( COUNT( DISTINCT [c].[PrayerRequestId] ) * 100.0 / NULLIF( ( SELECT COUNT( * ) FROM #Req ), 0 ) AS DECIMAL(5,1) ) AS [pctRequestsWithComment],
            CAST( AVG( CASE WHEN [c].[ParentNoteId] IS NOT NULL THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctCommentsThatAreReplies],
            CAST( AVG( CASE WHEN [c].[AuthorPersonId] = [r].[RequesterPersonId] THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctCommentsByRequester]
        FROM #Comment AS [c]
        INNER JOIN #Req AS [r] ON [r].[PrayerRequestId] = [c].[PrayerRequestId]
    ) AS [s]
    OUTER APPLY (
        SELECT TOP 1
            CAST( PERCENTILE_CONT( 0.50 ) WITHIN GROUP ( ORDER BY [x].[Comments] ) OVER () AS DECIMAL(7,1) ) AS [commentsPerCommentedRequestP50],
            CAST( PERCENTILE_CONT( 0.90 ) WITHIN GROUP ( ORDER BY [x].[Comments] ) OVER () AS DECIMAL(7,1) ) AS [commentsPerCommentedRequestP90],
            CAST( PERCENTILE_CONT( 0.50 ) WITHIN GROUP ( ORDER BY DATEDIFF( MINUTE, [x].[EnteredDateTime], [x].[FirstCommentDateTime] ) / 60.0 ) OVER () AS DECIMAL(9,1) ) AS [hoursToFirstCommentP50]
        FROM (
            SELECT [c].[PrayerRequestId], [r].[EnteredDateTime], COUNT( * ) AS [Comments], MIN( [c].[CreatedDateTime] ) AS [FirstCommentDateTime]
            FROM #Comment AS [c]
            INNER JOIN #Req AS [r] ON [r].[PrayerRequestId] = [c].[PrayerRequestId]
            GROUP BY [c].[PrayerRequestId], [r].[EnteredDateTime]
        ) AS [x]
    ) AS [cr]
    OUTER APPLY (
        SELECT TOP 1
            CAST( PERCENTILE_CONT( 0.50 ) WITHIN GROUP ( ORDER BY [c].[TextLength] ) OVER () AS DECIMAL(7,0) ) AS [commentLengthP50]
        FROM #Comment AS [c]
        WHERE [c].[TextLength] > 0
    ) AS [ct]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

-- =============================================
-- Output: one row per section. Rock's SQL editor shows only this result.
-- =============================================

DECLARE @JMeta NVARCHAR(MAX) = (
    SELECT
        @ChurchLabel AS [church],
        5 AS [part],
        CONVERT( CHAR(10), @Start24, 120 ) AS [periodStart],
        CONVERT( CHAR(10), @EndDate, 120 ) AS [periodEndExclusive],
        CAST( CASE WHEN @PrayerChannelId IS NULL THEN 0 ELSE 1 END AS BIT ) AS [hasPrayerChannel],
        CAST( CASE WHEN @PrayerCommentNoteTypeId IS NULL THEN 0 ELSE 1 END AS BIT ) AS [hasPrayerCommentNoteType]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

SELECT [s].[Section], [s].[Json]
FROM ( VALUES
    ( 1, 'meta', @JMeta ),
    ( 2, 'prayerSummary', @JPrayerSummary ),
    ( 3, 'prayerByMonth', @JPrayerByMonth ),
    ( 4, 'prayerDetail', @JPrayerDetail ),
    ( 5, 'prayerByWeekday', @JPrayerByWeekday ),
    ( 6, 'prayerByHourBand', @JPrayerByHourBand ),
    ( 7, 'prayerCategories', @JPrayerCategories ),
    ( 8, 'prayerRequesters', @JPrayerRequesters ),
    ( 9, 'prayerTeam', @JPrayerTeam ),
    ( 10, 'prayerComments', @JPrayerComments )
) AS [s] ( [Order], [Section], [Json] )
ORDER BY [s].[Order];

DROP TABLE IF EXISTS #Req, #Component, #Prayed, #ReqPrayed, #Comment;

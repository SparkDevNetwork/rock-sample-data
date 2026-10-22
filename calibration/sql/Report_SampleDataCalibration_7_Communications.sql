SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

-- =============================================
-- Description: Sample data calibration, part 7: communications in detail.
--
--              Part 3 counts every communication, and most of those go to
--              one person from a workflow. Part 7 measures the shape of mass
--              sends (25 or more recipients) and of text conversations:
--              how often a church sends, to how many, on which days and
--              hours, how many people open and click and how fast, who
--              unsubscribes, how people reply to texts, how long text
--              threads run, how fast staff answer, and how many people
--              have the church's app with notifications on.
--
--              Collects anonymous, aggregate statistics from a live Rock
--              database. Returns no names, no Ids, no list names, no message
--              text, no phone numbers, and no clicked URLs. Links are
--              compared by checksum only.
--
--              Every rate stands alone. Run part 2 on the same database only
--              if you also want monthly volumes, which are scaled by part
--              2's active adult count.
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
--              Built to finish inside Rock's 180 second limit on a church
--              with millions of recipients. Rock's recipient indexes do not
--              hold a recipient's status, so reading statuses means reading
--              whole rows, each with a copy of the message. The script
--              never does that for every recipient. Sizes come from the
--              CommunicationId index. Delivery, opens, clicks, and
--              unsubscribes come from a sample: the newest mass sends of
--              each medium in the last 3 months (@SampleSendsPerMedium).
--              Text threads read only the people who texted in.
--
--              Rock builds some of the indexes this needs in post-update
--              jobs, which can still be pending on a large database. Rock
--              18.0 drops the CommunicationRecipient index on
--              CommunicationId, and an 18.1 job puts it back. Without that
--              index, sizing every send would read the whole recipient
--              table, so the mass send sections are skipped and the meta
--              section says why. The people, app, and text sections still
--              run. In SSMS, which has no time limit, set @ReadWithoutIndex
--              to 1 to read the whole table anyway.
--
--              Use the same @ChurchLabel as in part 2.
--              Requires Rock 15 or later and SQL Server 2016 or later.
--              Unsubscribe and spam rates need Rock 17 or later, and
--              communication flows need Rock 18 or later. On older Rock
--              those sections come back empty.
-- Date: 2026-10-21
-- =============================================

-- Settings
DECLARE @ChurchLabel NVARCHAR(50) = 'Unlabeled'; -- Use a neutral label like source-2, never a church name.

-- A mass send has at least this many recipients.
DECLARE @MassSendMinRecipients INT = 25;

-- Delivery, opens, clicks, and unsubscribes are measured on this many of
-- the newest mass sends of each medium in the last 3 months.
DECLARE @SampleSendsPerMedium INT = 8;

-- Delivery and unsubscribes read whole recipient rows, so they use about
-- this many recipients from each sampled send, spread across the send.
DECLARE @SampleRecipientsPerSend INT = 1000;

-- 1 reads the whole recipient table when Rock's CommunicationId index is
-- missing. Use it only in SSMS: it takes far longer than Rock allows.
DECLARE @ReadWithoutIndex BIT = 0;

-- Text thread length and reply times are measured on this many of the
-- people who texted in, picked by a hash of their Id. A quarter as many
-- when Rock 16.6's recipient index is missing.
DECLARE @SampleTexters INT = 200;

-- Sends and texts use the last 12 complete months. The recipient sample
-- uses the last 3. The current month is excluded.
DECLARE @EndDate DATE = DATEFROMPARTS( YEAR( GETDATE() ), MONTH( GETDATE() ), 1 );
DECLARE @Start12 DATE = DATEADD( MONTH, -12, @EndDate );
DECLARE @Start3 DATE = DATEADD( MONTH, -3, @EndDate );

-- Lookups
DECLARE @SmsMediumId INT = ( SELECT TOP 1 [Id] FROM [EntityType] WHERE [Guid] = '4BC02764-512A-4A10-ACDE-586F71D8A8BD' );
DECLARE @CommunicationChannelId INT = ( SELECT TOP 1 [Id] FROM [InteractionChannel] WHERE [Guid] = 'C88A187F-0343-4E7C-AF3F-79A8989DFA65' );
DECLARE @ActiveRecordStatusId INT = ( SELECT TOP 1 [Id] FROM [DefinedValue] WHERE [Guid] = '618F906C-C33D-4FA3-8AEF-E58CB7B63F1E' );
DECLARE @PersonRecordTypeId INT = ( SELECT TOP 1 [Id] FROM [DefinedValue] WHERE [Guid] = '36CF10D6-C695-413D-8E7C-4546EFEF385E' );
DECLARE @CommunicationListGroupTypeId INT = ( SELECT TOP 1 [Id] FROM [GroupType] WHERE [Guid] = 'D1D95777-FFA3-CBB3-4A6D-658706DAED33' );

-- Does CommunicationRecipient have an index that starts with CommunicationId?
DECLARE @HasRecipientCommunicationIndex BIT = CASE WHEN EXISTS (
    SELECT 1
    FROM sys.indexes AS [i]
    INNER JOIN sys.index_columns AS [ic] ON [ic].[object_id] = [i].[object_id] AND [ic].[index_id] = [i].[index_id] AND [ic].[key_ordinal] = 1
    INNER JOIN sys.columns AS [col] ON [col].[object_id] = [ic].[object_id] AND [col].[column_id] = [ic].[column_id]
    WHERE [i].[object_id] = OBJECT_ID( 'CommunicationRecipient' ) AND [col].[name] = 'CommunicationId' AND [i].[has_filter] = 0
) THEN 1 ELSE 0 END;
DECLARE @CanReadRecipientsBySend BIT = CASE WHEN @HasRecipientCommunicationIndex = 1 OR @ReadWithoutIndex = 1 THEN 1 ELSE 0 END;

-- Clean up temp tables from an earlier run in the same SSMS session.
DROP TABLE IF EXISTS #CommId, #Size, #Mass, #Sample, #SampleRecipient, #MassOutcome, #Component, #Int, #Adult, #Resp, #Texter, #SmsOutRaw, #SmsSend, #SmsOut, #Flow;

-- =============================================
-- Sends (last 12 months)
-- =============================================

-- Rock's SendDateTime index holds the Id, so finding the year's sends never
-- reads a message body. That index comes from a post-update job, which may
-- not have run. Then the SendDateKey index (from a migration, so always
-- there) is used. Rock sets SendDateKey (yyyyMMdd) from SendDateTime on save.
DECLARE @HasSendDateTimeIndex BIT = CASE WHEN EXISTS (
    SELECT 1
    FROM sys.indexes AS [i]
    INNER JOIN sys.index_columns AS [ic] ON [ic].[object_id] = [i].[object_id] AND [ic].[index_id] = [i].[index_id] AND [ic].[key_ordinal] = 1
    INNER JOIN sys.columns AS [col] ON [col].[object_id] = [ic].[object_id] AND [col].[column_id] = [ic].[column_id]
    WHERE [i].[object_id] = OBJECT_ID( 'Communication' ) AND [col].[name] = 'SendDateTime' AND [i].[has_filter] = 0
) THEN 1 ELSE 0 END;
DECLARE @Start12Key INT = CAST( CONVERT( CHAR(8), @Start12, 112 ) AS INT );
DECLARE @EndKey INT = CAST( CONVERT( CHAR(8), @EndDate, 112 ) AS INT );

CREATE TABLE #CommId ( [CommunicationId] INT );

IF @HasSendDateTimeIndex = 1
BEGIN
    INSERT INTO #CommId ( [CommunicationId] )
    SELECT [c].[Id]
    FROM [Communication] AS [c]
    WHERE [c].[SendDateTime] >= @Start12 AND [c].[SendDateTime] < @EndDate;
END
ELSE
BEGIN
    INSERT INTO #CommId ( [CommunicationId] )
    SELECT [c].[Id]
    FROM [Communication] AS [c]
    WHERE [c].[SendDateKey] >= @Start12Key AND [c].[SendDateKey] < @EndKey;
END;

CREATE CLUSTERED INDEX [IX_CommunicationId] ON #CommId ( [CommunicationId] );

/*
    10/22/26 - CLAUDE

    CommunicationRecipient is often the largest table in the database, and
    each row holds a copy of the sent message. Rock's IX_CommunicationId
    index holds only CommunicationId and PersonAliasId. Communication Ids
    grow over time, so a year of sends is one range of Ids, and counting
    that range reads one ordered slice of the narrow index. Joining each
    communication to its recipients instead made SQL Server read whole
    recipient rows on a large church.

    Reason: The first version of part 7 timed out on a large church.
*/
DECLARE @MinCommunicationId INT = ( SELECT MIN( [CommunicationId] ) FROM #CommId );
DECLARE @MaxCommunicationId INT = ( SELECT MAX( [CommunicationId] ) FROM #CommId );

CREATE TABLE #Size ( [CommunicationId] INT, [Recipients] INT );

-- Without the index, #Size stays empty and every mass send section is skipped.
IF @CanReadRecipientsBySend = 1
BEGIN
    INSERT INTO #Size ( [CommunicationId], [Recipients] )
    SELECT
        [cr].[CommunicationId],
        COUNT( * )
    FROM [CommunicationRecipient] AS [cr]
    WHERE [cr].[CommunicationId] BETWEEN @MinCommunicationId AND @MaxCommunicationId
    GROUP BY [cr].[CommunicationId];
END;

-- The Id range can hold a draft or a send from outside the window.
DELETE [s]
FROM #Size AS [s]
WHERE NOT EXISTS ( SELECT 1 FROM #CommId AS [ci] WHERE [ci].[CommunicationId] = [s].[CommunicationId] );

CREATE CLUSTERED INDEX [IX_CommunicationId] ON #Size ( [CommunicationId] );

-- Only mass sends are read from the Communication table itself.
-- Communication type: 0 Recipient preference, 1 Email, 2 SMS, 3 Push.
SELECT
    [c].[Id] AS [CommunicationId],
    CASE [c].[CommunicationType] WHEN 1 THEN 'Email' WHEN 2 THEN 'SMS' WHEN 3 THEN 'Push' ELSE 'Recipient preference' END AS [Medium],
    [c].[SendDateTime],
    [s].[Recipients],
    [c].[IsBulkCommunication],
    CASE WHEN [c].[ListGroupId] IS NOT NULL THEN 1 ELSE 0 END AS [HasList],
    CASE WHEN ISNULL( [c].[Segments], '' ) <> '' THEN 1 ELSE 0 END AS [HasSegments],
    CASE WHEN [c].[CommunicationTemplateId] IS NOT NULL THEN 1 ELSE 0 END AS [HasTemplate],
    CASE WHEN [c].[CommunicationTopicValueId] IS NOT NULL THEN 1 ELSE 0 END AS [HasTopic]
INTO #Mass
FROM #Size AS [s]
INNER JOIN [Communication] AS [c] ON [c].[Id] = [s].[CommunicationId]
WHERE [s].[Recipients] >= @MassSendMinRecipients;

CREATE CLUSTERED INDEX [IX_CommunicationId] ON #Mass ( [CommunicationId] );

-- The recipient sample: the newest mass sends of each medium in the last 3 months.
SELECT [x].[CommunicationId], [x].[Medium]
INTO #Sample
FROM (
    SELECT
        [m].[CommunicationId],
        [m].[Medium],
        ROW_NUMBER() OVER ( PARTITION BY [m].[Medium] ORDER BY [m].[SendDateTime] DESC ) AS [Newest]
    FROM #Mass AS [m]
    WHERE [m].[SendDateTime] >= @Start3
) AS [x]
WHERE [x].[Newest] <= @SampleSendsPerMedium;

CREATE CLUSTERED INDEX [IX_CommunicationId] ON #Sample ( [CommunicationId] );

-- About @SampleRecipientsPerSend recipients from each sampled send, taken
-- every Nth recipient Id so the sample spans the whole send. The Ids come
-- from the narrow CommunicationId index, so no full rows are read here.
CREATE TABLE #SampleRecipient ( [CommunicationId] INT, [Medium] NVARCHAR(30), [RecipientId] INT );

IF @CanReadRecipientsBySend = 1
BEGIN
    INSERT INTO #SampleRecipient ( [CommunicationId], [Medium], [RecipientId] )
    SELECT
        [s].[CommunicationId],
        [s].[Medium],
        [cr].[Id]
    FROM #Sample AS [s]
    INNER JOIN #Mass AS [m] ON [m].[CommunicationId] = [s].[CommunicationId]
    INNER JOIN [CommunicationRecipient] AS [cr] ON [cr].[CommunicationId] = [s].[CommunicationId]
    WHERE [cr].[Id] % CEILING( [m].[Recipients] * 1.0 / @SampleRecipientsPerSend ) = 0;
END;

CREATE CLUSTERED INDEX [IX_RecipientId] ON #SampleRecipient ( [RecipientId] );

-- Living adults on active person records. Rates about people use them as
-- the base. Their count is never returned.
SELECT
    [p].[Id] AS [PersonId],
    CASE WHEN ISNULL( [p].[Email], '' ) <> '' THEN 1 ELSE 0 END AS [HasEmail],
    [p].[IsEmailActive],
    [p].[EmailPreference],
    [p].[CommunicationPreference]
INTO #Adult
FROM [Person] AS [p]
WHERE [p].[RecordTypeValueId] = @PersonRecordTypeId
    AND [p].[RecordStatusValueId] = @ActiveRecordStatusId
    AND [p].[IsDeceased] = 0
    AND [p].[AgeClassification] = 1;

CREATE CLUSTERED INDEX [IX_PersonId] ON #Adult ( [PersonId] );

DECLARE @Adults INT = ( SELECT COUNT( * ) FROM #Adult );

DECLARE @JMassSendSummary NVARCHAR(MAX) = (
    SELECT
        CAST( ( SELECT COUNT( * ) FROM #Mass ) * 100.0 / NULLIF( ( SELECT COUNT( * ) FROM #Size ), 0 ) AS DECIMAL(5,1) ) AS [pctOfSendsThatAreMass],
        CAST( ( SELECT SUM( CAST( [Recipients] AS BIGINT ) ) FROM #Mass ) * 100.0 / NULLIF( ( SELECT SUM( CAST( [Recipients] AS BIGINT ) ) FROM #Size ), 0 ) AS DECIMAL(5,1) ) AS [pctOfRecipientsInMassSends],
        CAST( ( SELECT AVG( CASE WHEN [IsBulkCommunication] = 1 THEN 100.0 ELSE 0.0 END ) FROM #Mass ) AS DECIMAL(5,1) ) AS [pctMassFlaggedBulk],
        CAST( ( SELECT AVG( [HasList] * 100.0 ) FROM #Mass ) AS DECIMAL(5,1) ) AS [pctMassWithList],
        CAST( ( SELECT AVG( [HasSegments] * 100.0 ) FROM #Mass ) AS DECIMAL(5,1) ) AS [pctMassWithSegments],
        CAST( ( SELECT AVG( [HasTemplate] * 100.0 ) FROM #Mass ) AS DECIMAL(5,1) ) AS [pctMassWithTemplate],
        CAST( ( SELECT AVG( [HasTopic] * 100.0 ) FROM #Mass ) AS DECIMAL(5,1) ) AS [pctMassWithTopic]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

-- Audience size is a share of living adults on active records, so it says
-- nothing about the church's size.
DECLARE @JMassSendsByMedium NVARCHAR(MAX) = (
    SELECT DISTINCT
        [m].[Medium] AS [medium],
        CAST( COUNT( * ) OVER ( PARTITION BY [m].[Medium] ) * 100.0 / NULLIF( ( SELECT COUNT( * ) FROM #Mass ), 0 ) AS DECIMAL(5,1) ) AS [pctOfMassSends],
        CAST( PERCENTILE_CONT( 0.50 ) WITHIN GROUP ( ORDER BY [m].[Recipients] * 100.0 / NULLIF( @Adults, 0 ) ) OVER ( PARTITION BY [m].[Medium] ) AS DECIMAL(6,1) ) AS [audiencePctOfAdultsP50],
        CAST( PERCENTILE_CONT( 0.90 ) WITHIN GROUP ( ORDER BY [m].[Recipients] * 100.0 / NULLIF( @Adults, 0 ) ) OVER ( PARTITION BY [m].[Medium] ) AS DECIMAL(6,1) ) AS [audiencePctOfAdultsP90],
        CAST( MAX( [m].[Recipients] * 100.0 / NULLIF( @Adults, 0 ) ) OVER ( PARTITION BY [m].[Medium] ) AS DECIMAL(6,1) ) AS [audiencePctOfAdultsMax]
    FROM #Mass AS [m]
    FOR JSON PATH
);

DECLARE @JMassSendSizeBands NVARCHAR(MAX) = (
    SELECT
        [b].[Medium] AS [medium],
        [b].[SizeBand] AS [sizeBand],
        CAST( COUNT( * ) * 100.0 / SUM( COUNT( * ) ) OVER ( PARTITION BY [b].[Medium] ) AS DECIMAL(5,1) ) AS [pctOfMediumSends]
    FROM (
        SELECT
            [m].[Medium],
            CASE
                WHEN [m].[Recipients] < 100 THEN '0025-0099'
                WHEN [m].[Recipients] < 500 THEN '0100-0499'
                WHEN [m].[Recipients] < 2000 THEN '0500-1999'
                WHEN [m].[Recipients] < 10000 THEN '2000-9999'
                ELSE '10000+'
            END AS [SizeBand]
        FROM #Mass AS [m]
    ) AS [b]
    GROUP BY [b].[Medium], [b].[SizeBand]
    ORDER BY [b].[Medium], [b].[SizeBand]
    FOR JSON PATH
);

DECLARE @JMassSendsByMonth NVARCHAR(MAX) = (
    SELECT
        CONVERT( CHAR(7), [m].[SendDateTime], 120 ) AS [month],
        [m].[Medium] AS [medium],
        COUNT( * ) AS [sends]
    FROM #Mass AS [m]
    GROUP BY CONVERT( CHAR(7), [m].[SendDateTime], 120 ), [m].[Medium]
    ORDER BY CONVERT( CHAR(7), [m].[SendDateTime], 120 ), [m].[Medium]
    FOR JSON PATH
);

-- Day 0 of DATEDIFF from 1900-01-01 is a Monday, so this works under any
-- server language setting.
DECLARE @JMassSendsByWeekday NVARCHAR(MAX) = (
    SELECT
        [w].[Medium] AS [medium],
        [w].[Weekday] AS [weekday],
        CAST( COUNT( * ) * 100.0 / SUM( COUNT( * ) ) OVER ( PARTITION BY [w].[Medium] ) AS DECIMAL(5,1) ) AS [pctOfMediumSends]
    FROM (
        SELECT
            [m].[Medium],
            CHOOSE( DATEDIFF( DAY, '19000101', [m].[SendDateTime] ) % 7 + 1, '1-Mon', '2-Tue', '3-Wed', '4-Thu', '5-Fri', '6-Sat', '7-Sun' ) AS [Weekday]
        FROM #Mass AS [m]
    ) AS [w]
    GROUP BY [w].[Medium], [w].[Weekday]
    ORDER BY [w].[Medium], [w].[Weekday]
    FOR JSON PATH
);

DECLARE @JMassSendsByHourBand NVARCHAR(MAX) = (
    SELECT
        [h].[Medium] AS [medium],
        [h].[HourBand] AS [hourBand],
        CAST( COUNT( * ) * 100.0 / SUM( COUNT( * ) ) OVER ( PARTITION BY [h].[Medium] ) AS DECIMAL(5,1) ) AS [pctOfMediumSends]
    FROM (
        SELECT
            [m].[Medium],
            CASE
                WHEN DATEPART( HOUR, [m].[SendDateTime] ) < 6 THEN '00-05'
                WHEN DATEPART( HOUR, [m].[SendDateTime] ) < 9 THEN '06-08'
                WHEN DATEPART( HOUR, [m].[SendDateTime] ) < 12 THEN '09-11'
                WHEN DATEPART( HOUR, [m].[SendDateTime] ) < 15 THEN '12-14'
                WHEN DATEPART( HOUR, [m].[SendDateTime] ) < 18 THEN '15-17'
                WHEN DATEPART( HOUR, [m].[SendDateTime] ) < 21 THEN '18-20'
                ELSE '21-23'
            END AS [HourBand]
        FROM #Mass AS [m]
    ) AS [h]
    GROUP BY [h].[Medium], [h].[HourBand]
    ORDER BY [h].[Medium], [h].[HourBand]
    FOR JSON PATH
);

-- Communication lists: sizes are shares of adults, never counts of people.
DECLARE @JCommunicationLists NVARCHAR(MAX) = (
    SELECT TOP 1
        COUNT( * ) OVER () AS [activeLists],
        CAST( PERCENTILE_CONT( 0.50 ) WITHIN GROUP ( ORDER BY [l].[Members] * 100.0 / NULLIF( @Adults, 0 ) ) OVER () AS DECIMAL(6,1) ) AS [membersPctOfAdultsP50],
        CAST( PERCENTILE_CONT( 0.90 ) WITHIN GROUP ( ORDER BY [l].[Members] * 100.0 / NULLIF( @Adults, 0 ) ) OVER () AS DECIMAL(6,1) ) AS [membersPctOfAdultsP90]
    FROM (
        SELECT
            [g].[Id],
            ( SELECT COUNT( * ) FROM [GroupMember] AS [gm] WHERE [gm].[GroupId] = [g].[Id] AND [gm].[GroupMemberStatus] = 1 AND [gm].[IsArchived] = 0 ) AS [Members]
        FROM [Group] AS [g]
        WHERE [g].[GroupTypeId] = @CommunicationListGroupTypeId
            AND [g].[IsActive] = 1
            AND [g].[IsArchived] = 0
    ) AS [l]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

-- =============================================
-- Recipient outcomes and unsubscribes (the sample)
-- =============================================

CREATE TABLE #MassOutcome (
    [CommunicationId] INT,
    [Medium] NVARCHAR(30),
    [Recipients] INT,
    [Delivered] INT,
    [Opened] INT,
    [Failed] INT,
    [Cancelled] INT,
    [Unsubscribed] DECIMAL(18,4),
    [Spam] DECIMAL(18,4),
    [LevelAll] DECIMAL(18,4),
    [LevelBulk] DECIMAL(18,4),
    [LevelList] DECIMAL(18,4)
);

/*
    10/22/26 - CLAUDE

    Status, opens, and unsubscribes are not in any recipient index that can
    be found by communication, so each recipient row is read in full. That
    is why only the sample is read, in one pass. The unsubscribe and spam
    columns arrived in Rock 17. Dynamic SQL lets the script compile on
    older Rock, where those columns come back empty.

    Reason: Keep the slowest read small and single, on every Rock version.
*/
DECLARE @UnsubscribeColumns NVARCHAR(MAX) =
    CASE WHEN COL_LENGTH( 'CommunicationRecipient', 'UnsubscribeDateTime' ) IS NOT NULL
        THEN N'
            SUM( CASE WHEN [cr].[UnsubscribeDateTime] IS NOT NULL THEN 1 ELSE 0 END ),
            SUM( CASE WHEN [cr].[SpamComplaintDateTime] IS NOT NULL THEN 1 ELSE 0 END ),
            SUM( CASE WHEN [cr].[UnsubscribeLevel] = 1 THEN 1 ELSE 0 END ),
            SUM( CASE WHEN [cr].[UnsubscribeLevel] = 2 THEN 1 ELSE 0 END ),
            SUM( CASE WHEN [cr].[UnsubscribeLevel] = 3 THEN 1 ELSE 0 END )'
        ELSE N'
            NULL, NULL, NULL, NULL, NULL'
    END;

-- Recipient status: 0 Pending, 1 Delivered, 2 Failed, 3 Cancelled, 4 Opened, 5 Sending.
DECLARE @OutcomeSql NVARCHAR(MAX) = N'
    INSERT INTO #MassOutcome
    SELECT
        [s].[CommunicationId],
        [s].[Medium],
        COUNT( * ),
        SUM( CASE WHEN [cr].[Status] IN ( 1, 4 ) THEN 1 ELSE 0 END ),
        SUM( CASE WHEN [cr].[Status] = 4 OR [cr].[OpenedDateTime] IS NOT NULL THEN 1 ELSE 0 END ),
        SUM( CASE WHEN [cr].[Status] = 2 THEN 1 ELSE 0 END ),
        SUM( CASE WHEN [cr].[Status] = 3 THEN 1 ELSE 0 END ),' + @UnsubscribeColumns + N'
    FROM #SampleRecipient AS [s]
    INNER JOIN [CommunicationRecipient] AS [cr] WITH ( FORCESEEK ) ON [cr].[Id] = [s].[RecipientId]
    GROUP BY [s].[CommunicationId], [s].[Medium];';

EXEC sp_executesql @OutcomeSql;

DECLARE @JMassOutcomes NVARCHAR(MAX) = (
    SELECT
        [o].[Medium] AS [medium],
        CAST( SUM( [o].[Delivered] ) * 100.0 / NULLIF( SUM( [o].[Recipients] ), 0 ) AS DECIMAL(5,1) ) AS [pctDelivered],
        CAST( SUM( [o].[Opened] ) * 100.0 / NULLIF( SUM( [o].[Delivered] ), 0 ) AS DECIMAL(5,1) ) AS [pctOpenedOfDelivered],
        CAST( SUM( [o].[Failed] ) * 100.0 / NULLIF( SUM( [o].[Recipients] ), 0 ) AS DECIMAL(5,1) ) AS [pctFailed],
        CAST( SUM( [o].[Cancelled] ) * 100.0 / NULLIF( SUM( [o].[Recipients] ), 0 ) AS DECIMAL(5,1) ) AS [pctCancelled]
    FROM #MassOutcome AS [o]
    GROUP BY [o].[Medium]
    FOR JSON PATH
);

-- =============================================
-- Opens and clicks (mass sends, last 3 months)
-- =============================================

-- Each communication has one interaction component whose EntityId is the
-- communication. Each interaction's EntityId is the recipient.
SELECT
    [ic].[Id] AS [InteractionComponentId],
    [s].[CommunicationId]
INTO #Component
FROM #Sample AS [s]
INNER JOIN [InteractionComponent] AS [ic] ON [ic].[EntityId] = [s].[CommunicationId] AND [ic].[InteractionChannelId] = @CommunicationChannelId;

/*
    10/21/26 - CLAUDE

    A click's InteractionData is its URL, which can name the church and its
    pages. Only a checksum of the URL is kept, to count distinct links and
    the share of clicks on the most clicked link, plus a flag for Rock's
    unsubscribe links. The URL itself never leaves the database.

    Reason: Measure link behavior without exposing the church's site.
*/
SELECT
    [c].[CommunicationId],
    [i].[EntityId] AS [RecipientId],
    [i].[Operation],
    [i].[InteractionDateTime],
    CASE WHEN [i].[Operation] = 'Click' THEN CHECKSUM( [i].[InteractionData] ) END AS [LinkHash],
    CASE WHEN [i].[Operation] = 'Click' AND [i].[InteractionData] LIKE '%unsubscribe%' THEN 1 ELSE 0 END AS [IsUnsubscribeLink]
INTO #Int
FROM #Component AS [c]
INNER JOIN [Interaction] AS [i] ON [i].[InteractionComponentId] = [c].[InteractionComponentId]
WHERE [i].[Operation] IN ( 'Opened', 'Click' );

CREATE CLUSTERED INDEX [IX_CommunicationId] ON #Int ( [CommunicationId], [RecipientId] );

-- Opens and clicks cover every recipient of the sampled sends. The delivered
-- count for the whole send is estimated from the recipient sample's rate.
DECLARE @JMassEngagement NVARCHAR(MAX) = (
    SELECT
        [o].[Medium] AS [medium],
        CAST( SUM( [e].[Openers] ) * 100.0 / NULLIF( SUM( [o].[Delivered] * 1.0 * [m].[Recipients] / NULLIF( [o].[Recipients], 0 ) ), 0 ) AS DECIMAL(5,1) ) AS [pctUniqueOpenersOfDelivered],
        CAST( SUM( [e].[Opens] ) * 1.0 / NULLIF( SUM( [e].[Openers] ), 0 ) AS DECIMAL(6,2) ) AS [opensPerOpener],
        CAST( SUM( [e].[Clickers] ) * 100.0 / NULLIF( SUM( [e].[Openers] ), 0 ) AS DECIMAL(5,1) ) AS [pctUniqueClickersOfOpeners],
        CAST( SUM( [e].[Clickers] ) * 100.0 / NULLIF( SUM( [o].[Delivered] * 1.0 * [m].[Recipients] / NULLIF( [o].[Recipients], 0 ) ), 0 ) AS DECIMAL(5,1) ) AS [pctUniqueClickersOfDelivered],
        CAST( SUM( [e].[Clicks] ) * 1.0 / NULLIF( SUM( [e].[Clickers] ), 0 ) AS DECIMAL(6,2) ) AS [clicksPerClicker],
        CAST( SUM( [e].[UnsubscribeClicks] ) * 100.0 / NULLIF( SUM( [e].[Clicks] ), 0 ) AS DECIMAL(5,1) ) AS [pctClicksOnUnsubscribeLinks]
    FROM #MassOutcome AS [o]
    INNER JOIN #Mass AS [m] ON [m].[CommunicationId] = [o].[CommunicationId]
    LEFT JOIN (
        SELECT
            [i].[CommunicationId],
            COUNT( DISTINCT CASE WHEN [i].[Operation] = 'Opened' THEN [i].[RecipientId] END ) AS [Openers],
            SUM( CASE WHEN [i].[Operation] = 'Opened' THEN 1 ELSE 0 END ) AS [Opens],
            COUNT( DISTINCT CASE WHEN [i].[Operation] = 'Click' THEN [i].[RecipientId] END ) AS [Clickers],
            SUM( CASE WHEN [i].[Operation] = 'Click' THEN 1 ELSE 0 END ) AS [Clicks],
            SUM( [i].[IsUnsubscribeLink] ) AS [UnsubscribeClicks]
        FROM #Int AS [i]
        GROUP BY [i].[CommunicationId]
    ) AS [e] ON [e].[CommunicationId] = [o].[CommunicationId]
    WHERE [o].[Medium] <> 'SMS'
    GROUP BY [o].[Medium]
    FOR JSON PATH
);

-- Links per email that got any clicks, and how much the top link draws.
DECLARE @JMassLinks NVARCHAR(MAX) = (
    SELECT TOP 1
        CAST( PERCENTILE_CONT( 0.50 ) WITHIN GROUP ( ORDER BY [l].[Links] ) OVER () AS DECIMAL(10,1) ) AS [linksClickedPerSendP50],
        CAST( PERCENTILE_CONT( 0.90 ) WITHIN GROUP ( ORDER BY [l].[Links] ) OVER () AS DECIMAL(10,1) ) AS [linksClickedPerSendP90],
        CAST( AVG( [l].[TopLinkShare] ) OVER () AS DECIMAL(5,1) ) AS [pctClicksOnTopLinkAvg]
    FROM (
        SELECT
            [k].[CommunicationId],
            COUNT( * ) AS [Links],
            MAX( [k].[Clicks] ) * 100.0 / SUM( [k].[Clicks] ) AS [TopLinkShare]
        FROM (
            SELECT [i].[CommunicationId], [i].[LinkHash], COUNT( * ) AS [Clicks]
            FROM #Int AS [i]
            WHERE [i].[Operation] = 'Click' AND [i].[IsUnsubscribeLink] = 0
            GROUP BY [i].[CommunicationId], [i].[LinkHash]
        ) AS [k]
        GROUP BY [k].[CommunicationId]
    ) AS [l]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

-- How soon each recipient's first open and first click follow the send.
DECLARE @JMassResponseDelay NVARCHAR(MAX) = (
    SELECT
        [d].[Medium] AS [medium],
        [d].[Operation] AS [operation],
        [d].[DelayBand] AS [delayBand],
        CAST( COUNT( * ) * 100.0 / SUM( COUNT( * ) ) OVER ( PARTITION BY [d].[Medium], [d].[Operation] ) AS DECIMAL(5,1) ) AS [pctOfFirsts]
    FROM (
        SELECT
            [m].[Medium],
            [f].[Operation],
            CASE
                WHEN DATEDIFF( MINUTE, [m].[SendDateTime], [f].[FirstDateTime] ) < 60 THEN '1-under 1 hour'
                WHEN DATEDIFF( MINUTE, [m].[SendDateTime], [f].[FirstDateTime] ) < 360 THEN '2-1 to 6 hours'
                WHEN DATEDIFF( MINUTE, [m].[SendDateTime], [f].[FirstDateTime] ) < 1440 THEN '3-6 to 24 hours'
                WHEN DATEDIFF( MINUTE, [m].[SendDateTime], [f].[FirstDateTime] ) < 4320 THEN '4-1 to 3 days'
                WHEN DATEDIFF( MINUTE, [m].[SendDateTime], [f].[FirstDateTime] ) < 10080 THEN '5-3 to 7 days'
                ELSE '6-7 days or more'
            END AS [DelayBand]
        FROM (
            SELECT [i].[CommunicationId], [i].[RecipientId], [i].[Operation], MIN( [i].[InteractionDateTime] ) AS [FirstDateTime]
            FROM #Int AS [i]
            WHERE [i].[IsUnsubscribeLink] = 0
            GROUP BY [i].[CommunicationId], [i].[RecipientId], [i].[Operation]
        ) AS [f]
        INNER JOIN #Mass AS [m] ON [m].[CommunicationId] = [f].[CommunicationId]
    ) AS [d]
    GROUP BY [d].[Medium], [d].[Operation], [d].[DelayBand]
    ORDER BY [d].[Medium], [d].[Operation], [d].[DelayBand]
    FOR JSON PATH
);

-- Unsubscribes and spam complaints (Rock 17+).
/*
    10/22/26 - CLAUDE

    Unsubscribes are rare, so a sample of a few thousand recipients gives a
    noisy rate. Rock 19 adds a filtered index that holds only recipients who
    unsubscribed, with their CommunicationId and level. When it exists,
    unsubscribes are counted over every recipient of the sampled sends,
    reading only that small index. The WHERE clause repeats the index's
    filter so SQL Server can use it. Without it, the recipient sample's
    counts stay. Spam complaints have no index, so they always come from
    the sample.

    Reason: An exact unsubscribe rate at almost no cost where Rock allows it.
*/
IF EXISTS ( SELECT 1 FROM sys.indexes WHERE [name] = 'IX_UnsubscribeDateTime_SendDateTime' AND [object_id] = OBJECT_ID( 'CommunicationRecipient' ) )
BEGIN
    EXEC sp_executesql N'
        UPDATE [o]
        SET
            [o].[Unsubscribed] = ISNULL( [u].[Unsubscribed], 0 ) * 1.0 * [o].[Recipients] / NULLIF( [m].[Recipients], 0 ),
            [o].[LevelAll] = ISNULL( [u].[LevelAll], 0 ) * 1.0 * [o].[Recipients] / NULLIF( [m].[Recipients], 0 ),
            [o].[LevelBulk] = ISNULL( [u].[LevelBulk], 0 ) * 1.0 * [o].[Recipients] / NULLIF( [m].[Recipients], 0 ),
            [o].[LevelList] = ISNULL( [u].[LevelList], 0 ) * 1.0 * [o].[Recipients] / NULLIF( [m].[Recipients], 0 )
        FROM #MassOutcome AS [o]
        INNER JOIN #Mass AS [m] ON [m].[CommunicationId] = [o].[CommunicationId]
        LEFT JOIN (
            SELECT
                [cr].[CommunicationId],
                COUNT( * ) AS [Unsubscribed],
                SUM( CASE WHEN [cr].[UnsubscribeLevel] = 1 THEN 1 ELSE 0 END ) AS [LevelAll],
                SUM( CASE WHEN [cr].[UnsubscribeLevel] = 2 THEN 1 ELSE 0 END ) AS [LevelBulk],
                SUM( CASE WHEN [cr].[UnsubscribeLevel] = 3 THEN 1 ELSE 0 END ) AS [LevelList]
            FROM [CommunicationRecipient] AS [cr] WITH ( INDEX( [IX_UnsubscribeDateTime_SendDateTime] ) )
            WHERE [cr].[UnsubscribeDateTime] IS NOT NULL
                AND [cr].[SendDateTime] IS NOT NULL
                AND [cr].[UnsubscribeLevel] IS NOT NULL
                AND [cr].[PersonAliasId] IS NOT NULL
                AND [cr].[CommunicationId] IN ( SELECT [s].[CommunicationId] FROM #Sample AS [s] )
            GROUP BY [cr].[CommunicationId]
        ) AS [u] ON [u].[CommunicationId] = [o].[CommunicationId];';
END;

DECLARE @JMassUnsubscribes NVARCHAR(MAX) = (
    SELECT
        [u].[Medium] AS [medium],
        CAST( [u].[Unsubscribed] * 100.0 / NULLIF( [u].[Recipients], 0 ) AS DECIMAL(6,2) ) AS [pctUnsubscribed],
        CAST( [u].[Spam] * 100.0 / NULLIF( [u].[Recipients], 0 ) AS DECIMAL(6,3) ) AS [pctSpamComplaints],
        CAST( [u].[LevelAll] * 100.0 / NULLIF( [u].[Unsubscribed], 0 ) AS DECIMAL(5,1) ) AS [pctUnsubscribesFromAll],
        CAST( [u].[LevelBulk] * 100.0 / NULLIF( [u].[Unsubscribed], 0 ) AS DECIMAL(5,1) ) AS [pctUnsubscribesFromBulk],
        CAST( [u].[LevelList] * 100.0 / NULLIF( [u].[Unsubscribed], 0 ) AS DECIMAL(5,1) ) AS [pctUnsubscribesFromList]
    FROM (
        SELECT [o].[Medium], SUM( [o].[Recipients] ) AS [Recipients], SUM( [o].[Unsubscribed] ) AS [Unsubscribed], SUM( [o].[Spam] ) AS [Spam],
            SUM( [o].[LevelAll] ) AS [LevelAll], SUM( [o].[LevelBulk] ) AS [LevelBulk], SUM( [o].[LevelList] ) AS [LevelList]
        FROM #MassOutcome AS [o]
        WHERE [o].[Medium] IN ( 'Email', 'Recipient preference' ) AND [o].[Unsubscribed] IS NOT NULL
        GROUP BY [o].[Medium]
    ) AS [u]
    FOR JSON PATH
);

-- =============================================
-- People: preferences, phones, and the church app
-- =============================================

-- Communication preference on a person: 1 Email, 2 SMS, 3 Push.
-- Email preference: 0 Email allowed, 1 No mass emails, 2 Do not email.
-- Site type 1 is a mobile application.
DECLARE @JPeople NVARCHAR(MAX) = (
    SELECT
        CAST( AVG( [a].[HasEmail] * 100.0 ) AS DECIMAL(5,1) ) AS [pctWithEmail],
        CAST( SUM( CASE WHEN [a].[HasEmail] = 1 AND [a].[IsEmailActive] = 0 THEN 1 ELSE 0 END ) * 100.0 / NULLIF( SUM( [a].[HasEmail] ), 0 ) AS DECIMAL(5,1) ) AS [pctEmailsInactive],
        CAST( AVG( CASE WHEN [a].[EmailPreference] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctNoMassEmails],
        CAST( AVG( CASE WHEN [a].[EmailPreference] = 2 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctDoNotEmail],
        CAST( AVG( CASE WHEN [a].[CommunicationPreference] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctPreferEmail],
        CAST( AVG( CASE WHEN [a].[CommunicationPreference] = 2 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctPreferSms],
        CAST( AVG( CASE WHEN [a].[CommunicationPreference] = 3 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctPreferPush],
        CAST( AVG( [a].[HasMessagingPhone] * 100.0 ) AS DECIMAL(5,1) ) AS [pctWithMessagingPhone],
        CAST( AVG( [a].[HasAppDevice] * 100.0 ) AS DECIMAL(5,1) ) AS [pctWithAppDevice],
        CAST( AVG( [a].[HasPushReadyDevice] * 100.0 ) AS DECIMAL(5,1) ) AS [pctWithPushReadyDevice]
    FROM (
        -- SQL Server does not allow a subquery inside AVG, so the flags are set here first.
        SELECT
            [ad].*,
            CASE WHEN EXISTS (
                SELECT 1 FROM [PhoneNumber] AS [pn] WHERE [pn].[PersonId] = [ad].[PersonId] AND [pn].[IsMessagingEnabled] = 1
            ) THEN 1 ELSE 0 END AS [HasMessagingPhone],
            CASE WHEN EXISTS (
                SELECT 1
                FROM [PersonalDevice] AS [pd]
                INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [pd].[PersonAliasId]
                INNER JOIN [Site] AS [s] ON [s].[Id] = [pd].[SiteId]
                WHERE [pa].[PersonId] = [ad].[PersonId] AND [pd].[IsActive] = 1 AND [s].[SiteType] = 1
            ) THEN 1 ELSE 0 END AS [HasAppDevice],
            CASE WHEN EXISTS (
                SELECT 1
                FROM [PersonalDevice] AS [pd]
                INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [pd].[PersonAliasId]
                INNER JOIN [Site] AS [s] ON [s].[Id] = [pd].[SiteId]
                WHERE [pa].[PersonId] = [ad].[PersonId] AND [pd].[IsActive] = 1 AND [s].[SiteType] = 1
                    AND [pd].[NotificationsEnabled] = 1 AND ISNULL( [pd].[DeviceRegistrationId], '' ) <> ''
            ) THEN 1 ELSE 0 END AS [HasPushReadyDevice]
        FROM #Adult AS [ad]
    ) AS [a]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

-- Platform mix of active app devices, by Rock's platform Guids.
DECLARE @JAppDevices NVARCHAR(MAX) = (
    SELECT
        CAST( AVG( CASE WHEN [dv].[Guid] = 'F00515E7-4EF3-480D-A45D-372CE3D80E69' THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctIos],
        CAST( AVG( CASE WHEN [dv].[Guid] = '63464BB8-83E2-4914-B922-5075311758F9' THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctAndroid],
        CAST( AVG( CASE WHEN [pd].[NotificationsEnabled] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctNotificationsOn],
        CAST( AVG( CASE WHEN [pd].[LastSeenDateTime] >= DATEADD( DAY, -30, @EndDate ) THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctSeenInLast30Days]
    FROM [PersonalDevice] AS [pd]
    INNER JOIN [Site] AS [s] ON [s].[Id] = [pd].[SiteId]
    LEFT JOIN [DefinedValue] AS [dv] ON [dv].[Id] = [pd].[PlatformValueId]
    WHERE [pd].[IsActive] = 1 AND [s].[SiteType] = 1
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

-- =============================================
-- Texts: replies and conversations (last 12 months)
-- =============================================

-- Inbound texts. A reply that is only a stop word counts as an opt-out.
SELECT
    [r].[Id] AS [ResponseId],
    [pa].[PersonId],
    [r].[RelatedSmsFromSystemPhoneNumberId] AS [NumberId],
    [r].[CreatedDateTime],
    [r].[IsRead],
    LEN( ISNULL( [r].[Response], '' ) ) AS [Length],
    CASE WHEN UPPER( LTRIM( RTRIM( ISNULL( [r].[Response], '' ) ) ) ) IN ( 'STOP', 'STOPALL', 'UNSUBSCRIBE', 'CANCEL', 'END', 'QUIT' ) THEN 1 ELSE 0 END AS [IsStop],
    [r].[RelatedCommunicationId]
INTO #Resp
FROM [CommunicationResponse] AS [r]
LEFT JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [r].[FromPersonAliasId]
WHERE [r].[RelatedMediumEntityTypeId] = @SmsMediumId
    AND [r].[CreatedDateTime] >= @Start12 AND [r].[CreatedDateTime] < @EndDate;

CREATE CLUSTERED INDEX [IX_Thread] ON #Resp ( [PersonId], [NumberId], [CreatedDateTime] );

/*
    10/22/26 - CLAUDE

    SMS Conversations shows outbound texts from a recipient's CreatedDateTime,
    so this does too. Only sends to one or two people count as conversation
    messages; mass texts are measured above. When send sizes are unknown (no
    CommunicationId index), a send not flagged as bulk counts as one.

    A thread needs an inbound text, so only people who texted in are read,
    and only a sample of them (@SampleTexters, picked by a hash of the Id).
    Recipient Ids grow over time, so the first recipient created in the
    window marks where the year's rows start; Rock's
    IX_ResponseCodeCreatedDateTime index (from a migration, so always there)
    finds it at once, and each person's rows are read from that Id on.
    Their texts are collected first, and only then are those sends'
    Communication rows read, so no message body is read for an email.

    Reason: The first versions read every texter's recipient rows and their
    Communication rows, and timed out on a large church.
*/
-- Rock 16.6's recipient index on Status, PersonAliasId, MediumEntityTypeId,
-- and CreatedDateTime finds a person's texts and holds the message, so no
-- full rows are read. It comes from a post-update job, so without it the
-- PersonAliasId index (from a migration) is used, with each row looked up.
-- The index is named outright: left to choose, SQL Server read every
-- recipient from the year's first Id on.
DECLARE @TextIndex SYSNAME = CASE
    WHEN EXISTS ( SELECT 1 FROM sys.indexes WHERE [name] = 'IX_Status_PersonAliasId_MediumEntityTypeId_CreatedDateTime' AND [object_id] = OBJECT_ID( 'CommunicationRecipient' ) )
        THEN 'IX_Status_PersonAliasId_MediumEntityTypeId_CreatedDateTime'
    ELSE 'IX_PersonAliasId'
END;

-- Without that index each texter's rows are looked up one by one, so fewer
-- texters are sampled.
IF @TextIndex = 'IX_PersonAliasId'
BEGIN
    SET @SampleTexters = @SampleTexters / 4;
END;

SELECT TOP ( @SampleTexters ) [t].[PersonId]
INTO #Texter
FROM ( SELECT DISTINCT [r].[PersonId] FROM #Resp AS [r] WHERE [r].[PersonId] IS NOT NULL ) AS [t]
ORDER BY CHECKSUM( [t].[PersonId] * 7919 );

CREATE CLUSTERED INDEX [IX_PersonId] ON #Texter ( [PersonId] );

DECLARE @MinRecipientId INT = ISNULL( (
    SELECT TOP 1 [cr].[Id]
    FROM [CommunicationRecipient] AS [cr]
    WHERE [cr].[ResponseCode] IS NULL AND [cr].[CreatedDateTime] >= @Start12
    ORDER BY [cr].[CreatedDateTime]
), 2147483647 );


CREATE TABLE #SmsOutRaw ( [PersonId] INT, [CommunicationId] INT, [CreatedDateTime] DATETIME, [Length] INT );

DECLARE @TextSql NVARCHAR(MAX) = N'
    INSERT INTO #SmsOutRaw ( [PersonId], [CommunicationId], [CreatedDateTime], [Length] )
    SELECT
        [t].[PersonId],
        [cr].[CommunicationId],
        [cr].[CreatedDateTime],
        LEN( ISNULL( [cr].[SentMessage], '''' ) )
    FROM #Texter AS [t]
    INNER JOIN [PersonAlias] AS [pa] ON [pa].[PersonId] = [t].[PersonId]
    INNER JOIN [CommunicationRecipient] AS [cr] WITH ( INDEX( ' + QUOTENAME( @TextIndex ) + N' ) ) ON [cr].[PersonAliasId] = [pa].[Id]
    WHERE [cr].[Id] >= @MinRecipientId
        AND [cr].[Status] IN ( 1, 4 )
        AND [cr].[MediumEntityTypeId] = @SmsMediumId
        AND [cr].[CreatedDateTime] >= @Start12 AND [cr].[CreatedDateTime] < @EndDate;';

EXEC sp_executesql @TextSql,
    N'@MinRecipientId INT, @SmsMediumId INT, @Start12 DATE, @EndDate DATE',
    @MinRecipientId = @MinRecipientId, @SmsMediumId = @SmsMediumId, @Start12 = @Start12, @EndDate = @EndDate;

-- Each send's Communication row is read once, not once per text, and a
-- send known to be a mass send is never read.
SELECT
    [x].[CommunicationId],
    [c].[SmsFromSystemPhoneNumberId] AS [NumberId]
INTO #SmsSend
FROM (
    SELECT DISTINCT [o].[CommunicationId]
    FROM #SmsOutRaw AS [o]
    LEFT JOIN #Size AS [s] ON [s].[CommunicationId] = [o].[CommunicationId]
    WHERE [s].[Recipients] <= 2 OR @CanReadRecipientsBySend = 0
) AS [x]
INNER JOIN [Communication] AS [c] WITH ( FORCESEEK ) ON [c].[Id] = [x].[CommunicationId]
WHERE [c].[SmsFromSystemPhoneNumberId] IS NOT NULL
    AND ( @CanReadRecipientsBySend = 1 OR [c].[IsBulkCommunication] = 0 );

SELECT
    [o].[PersonId],
    [ss].[NumberId],
    [o].[CreatedDateTime],
    [o].[Length]
INTO #SmsOut
FROM #SmsOutRaw AS [o]
INNER JOIN #SmsSend AS [ss] ON [ss].[CommunicationId] = [o].[CommunicationId];

CREATE CLUSTERED INDEX [IX_Thread] ON #SmsOut ( [PersonId], [NumberId], [CreatedDateTime] );

DECLARE @JSmsInbound NVARCHAR(MAX) = (
    SELECT TOP 1
        CAST( AVG( CASE WHEN [r].[PersonId] IS NOT NULL THEN 100.0 ELSE 0.0 END ) OVER () AS DECIMAL(5,1) ) AS [pctFromKnownPerson],
        CAST( AVG( CASE WHEN [r].[RelatedCommunicationId] IS NOT NULL THEN 100.0 ELSE 0.0 END ) OVER () AS DECIMAL(5,1) ) AS [pctLinkedToCommunication],
        CAST( AVG( [r].[IsStop] * 100.0 ) OVER () AS DECIMAL(5,1) ) AS [pctStopWords],
        CAST( AVG( CASE WHEN [r].[IsRead] = 0 THEN 100.0 ELSE 0.0 END ) OVER () AS DECIMAL(5,1) ) AS [pctUnread],
        CAST( PERCENTILE_CONT( 0.50 ) WITHIN GROUP ( ORDER BY [r].[Length] ) OVER () AS DECIMAL(10,1) ) AS [lengthP50],
        CAST( PERCENTILE_CONT( 0.90 ) WITHIN GROUP ( ORDER BY [r].[Length] ) OVER () AS DECIMAL(10,1) ) AS [lengthP90]
    FROM #Resp AS [r]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

DECLARE @JSmsInboundByMonth NVARCHAR(MAX) = (
    SELECT
        CONVERT( CHAR(7), [r].[CreatedDateTime], 120 ) AS [month],
        COUNT( * ) AS [texts]
    FROM #Resp AS [r]
    GROUP BY CONVERT( CHAR(7), [r].[CreatedDateTime], 120 )
    ORDER BY CONVERT( CHAR(7), [r].[CreatedDateTime], 120 )
    FOR JSON PATH
);

DECLARE @JSmsInboundTiming NVARCHAR(MAX) = (
    SELECT
        [t].[Kind] AS [kind],
        [t].[Value] AS [value],
        CAST( COUNT( * ) * 100.0 / SUM( COUNT( * ) ) OVER ( PARTITION BY [t].[Kind] ) AS DECIMAL(5,1) ) AS [pctTexts]
    FROM (
        SELECT 'weekday' AS [Kind], CHOOSE( DATEDIFF( DAY, '19000101', [r].[CreatedDateTime] ) % 7 + 1, '1-Mon', '2-Tue', '3-Wed', '4-Thu', '5-Fri', '6-Sat', '7-Sun' ) AS [Value]
        FROM #Resp AS [r]
        UNION ALL
        SELECT 'hourBand', CASE
                WHEN DATEPART( HOUR, [r].[CreatedDateTime] ) < 6 THEN '00-05'
                WHEN DATEPART( HOUR, [r].[CreatedDateTime] ) < 9 THEN '06-08'
                WHEN DATEPART( HOUR, [r].[CreatedDateTime] ) < 12 THEN '09-11'
                WHEN DATEPART( HOUR, [r].[CreatedDateTime] ) < 15 THEN '12-14'
                WHEN DATEPART( HOUR, [r].[CreatedDateTime] ) < 18 THEN '15-17'
                WHEN DATEPART( HOUR, [r].[CreatedDateTime] ) < 21 THEN '18-20'
                ELSE '21-23'
            END
        FROM #Resp AS [r]
    ) AS [t]
    GROUP BY [t].[Kind], [t].[Value]
    ORDER BY [t].[Kind], [t].[Value]
    FOR JSON PATH
);

-- Replies to mass texts, matched through RelatedCommunicationId.
DECLARE @JSmsMassReplies NVARCHAR(MAX) = (
    SELECT
        CAST( AVG( CASE WHEN [x].[Replies] > 0 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctOfMassTextsWithAnyReply],
        CAST( SUM( [x].[Replies] ) * 100.0 / NULLIF( SUM( [x].[Recipients] ), 0 ) AS DECIMAL(6,2) ) AS [repliesPer100Recipients],
        CAST( SUM( [x].[StopReplies] ) * 100.0 / NULLIF( SUM( [x].[Recipients] ), 0 ) AS DECIMAL(6,2) ) AS [stopRepliesPer100Recipients]
    FROM (
        SELECT
            [m].[CommunicationId],
            [m].[Recipients],
            ( SELECT COUNT( * ) FROM #Resp AS [r] WHERE [r].[RelatedCommunicationId] = [m].[CommunicationId] ) AS [Replies],
            ( SELECT COUNT( * ) FROM #Resp AS [r] WHERE [r].[RelatedCommunicationId] = [m].[CommunicationId] AND [r].[IsStop] = 1 ) AS [StopReplies]
        FROM #Mass AS [m]
        WHERE [m].[Medium] = 'SMS'
    ) AS [x]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

-- A thread is one person and one church number with at least one inbound text.
DECLARE @JSmsThreads NVARCHAR(MAX) = (
    SELECT
        CAST( AVG( CASE WHEN [t].[Messages] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctThreadsWith1Message],
        CAST( AVG( CASE WHEN [t].[Messages] BETWEEN 2 AND 3 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctThreadsWith2To3Messages],
        CAST( AVG( CASE WHEN [t].[Messages] BETWEEN 4 AND 9 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctThreadsWith4To9Messages],
        CAST( AVG( CASE WHEN [t].[Messages] >= 10 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctThreadsWith10PlusMessages],
        CAST( AVG( CASE WHEN [t].[Outbound] > 0 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctThreadsWithStaffText],
        CAST( AVG( CASE WHEN [t].[Unread] > 0 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctThreadsWithUnread],
        CAST( AVG( CASE WHEN [spn].[AssignedToPersonAliasId] IS NOT NULL THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctThreadsOnPersonalNumber]
    FROM (
        SELECT
            [r].[PersonId],
            [r].[NumberId],
            COUNT( * ) + ( SELECT COUNT( * ) FROM #SmsOut AS [o] WHERE [o].[PersonId] = [r].[PersonId] AND [o].[NumberId] = [r].[NumberId] ) AS [Messages],
            ( SELECT COUNT( * ) FROM #SmsOut AS [o] WHERE [o].[PersonId] = [r].[PersonId] AND [o].[NumberId] = [r].[NumberId] ) AS [Outbound],
            SUM( CASE WHEN [r].[IsRead] = 0 THEN 1 ELSE 0 END ) AS [Unread]
        FROM #Resp AS [r]
        WHERE [r].[PersonId] IN ( SELECT [x].[PersonId] FROM #Texter AS [x] ) AND [r].[NumberId] IS NOT NULL
        GROUP BY [r].[PersonId], [r].[NumberId]
    ) AS [t]
    LEFT JOIN [SystemPhoneNumber] AS [spn] ON [spn].[Id] = [t].[NumberId]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

-- How fast the church answers an inbound text, and how fast a person answers
-- a one-to-one text from the church. Only answers within 7 days count.
DECLARE @JSmsReplyDelay NVARCHAR(MAX) = (
    SELECT
        [d].[Direction] AS [direction],
        CAST( AVG( CASE WHEN [d].[Minutes] IS NOT NULL THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctAnswered],
        CAST( AVG( CASE WHEN [d].[Minutes] < 15 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctUnder15Minutes],
        CAST( AVG( CASE WHEN [d].[Minutes] >= 15 AND [d].[Minutes] < 60 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pct15To60Minutes],
        CAST( AVG( CASE WHEN [d].[Minutes] >= 60 AND [d].[Minutes] < 240 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pct1To4Hours],
        CAST( AVG( CASE WHEN [d].[Minutes] >= 240 AND [d].[Minutes] < 1440 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pct4To24Hours],
        CAST( AVG( CASE WHEN [d].[Minutes] >= 1440 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pct1To7Days]
    FROM (
        SELECT
            'churchToPerson' AS [Direction],
            DATEDIFF( MINUTE, [r].[CreatedDateTime], [n].[CreatedDateTime] ) AS [Minutes]
        FROM #Resp AS [r]
        OUTER APPLY (
            SELECT TOP 1 [o].[CreatedDateTime]
            FROM #SmsOut AS [o]
            WHERE [o].[PersonId] = [r].[PersonId] AND [o].[NumberId] = [r].[NumberId]
                AND [o].[CreatedDateTime] > [r].[CreatedDateTime]
                AND [o].[CreatedDateTime] < DATEADD( DAY, 7, [r].[CreatedDateTime] )
            ORDER BY [o].[CreatedDateTime]
        ) AS [n]
        WHERE [r].[PersonId] IN ( SELECT [x].[PersonId] FROM #Texter AS [x] ) AND [r].[IsStop] = 0
        UNION ALL
        SELECT
            'personToChurch',
            DATEDIFF( MINUTE, [o].[CreatedDateTime], [n].[CreatedDateTime] )
        FROM #SmsOut AS [o]
        OUTER APPLY (
            SELECT TOP 1 [r].[CreatedDateTime]
            FROM #Resp AS [r]
            WHERE [r].[PersonId] = [o].[PersonId] AND [r].[NumberId] = [o].[NumberId]
                AND [r].[CreatedDateTime] > [o].[CreatedDateTime]
                AND [r].[CreatedDateTime] < DATEADD( DAY, 7, [o].[CreatedDateTime] )
            ORDER BY [r].[CreatedDateTime]
        ) AS [n]
    ) AS [d]
    GROUP BY [d].[Direction]
    FOR JSON PATH
);

DECLARE @JSmsOutboundLength NVARCHAR(MAX) = (
    SELECT TOP 1
        CAST( PERCENTILE_CONT( 0.50 ) WITHIN GROUP ( ORDER BY [o].[Length] ) OVER () AS DECIMAL(10,1) ) AS [lengthP50],
        CAST( PERCENTILE_CONT( 0.90 ) WITHIN GROUP ( ORDER BY [o].[Length] ) OVER () AS DECIMAL(10,1) ) AS [lengthP90]
    FROM #SmsOut AS [o]
    WHERE [o].[Length] > 0
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

-- Church phone numbers are setup, not people, so counts are fine here.
DECLARE @JSystemPhoneNumbers NVARCHAR(MAX) = (
    SELECT
        COUNT( * ) AS [activeNumbers],
        SUM( CASE WHEN [spn].[IsSmsEnabled] = 1 THEN 1 ELSE 0 END ) AS [smsEnabledNumbers],
        SUM( CASE WHEN [spn].[AssignedToPersonAliasId] IS NOT NULL THEN 1 ELSE 0 END ) AS [personalNumbers]
    FROM [SystemPhoneNumber] AS [spn]
    WHERE [spn].[IsActive] = 1
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

-- =============================================
-- Communication flows (Rock 18+)
-- =============================================

CREATE TABLE #Flow (
    [Flows] INT,
    [ActiveFlows] INT,
    [CommunicationsPerFlowP50] DECIMAL(6,1)
);

IF OBJECT_ID( 'CommunicationFlow' ) IS NOT NULL AND OBJECT_ID( 'CommunicationFlowCommunication' ) IS NOT NULL
BEGIN
    EXEC sp_executesql N'
        INSERT INTO #Flow ( [Flows], [ActiveFlows], [CommunicationsPerFlowP50] )
        SELECT TOP 1
            COUNT( * ) OVER (),
            SUM( CASE WHEN [f].[IsActive] = 1 THEN 1 ELSE 0 END ) OVER (),
            CAST( PERCENTILE_CONT( 0.50 ) WITHIN GROUP ( ORDER BY [f].[Messages] ) OVER () AS DECIMAL(6,1) )
        FROM (
            SELECT
                [cf].[Id],
                [cf].[IsActive],
                ( SELECT COUNT( * ) FROM [CommunicationFlowCommunication] AS [cfc] WHERE [cfc].[CommunicationFlowId] = [cf].[Id] ) AS [Messages]
            FROM [CommunicationFlow] AS [cf]
        ) AS [f];';
END;

DECLARE @JFlows NVARCHAR(MAX) = (
    SELECT
        [f].[Flows] AS [flows],
        [f].[ActiveFlows] AS [activeFlows],
        [f].[CommunicationsPerFlowP50] AS [communicationsPerFlowP50]
    FROM #Flow AS [f]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

-- =============================================
-- Output: one row per section. Rock's SQL editor shows only this result.
-- =============================================

DECLARE @JMeta NVARCHAR(MAX) = (
    SELECT
        @ChurchLabel AS [church],
        7 AS [part],
        @MassSendMinRecipients AS [massSendMinRecipients],
        @SampleSendsPerMedium AS [sampleSendsPerMedium],
        @SampleRecipientsPerSend AS [sampleRecipientsPerSend],
        @SampleTexters AS [sampleTexters],
        CAST( @HasRecipientCommunicationIndex AS BIT ) AS [hasRecipientCommunicationIndex],
        CASE WHEN @CanReadRecipientsBySend = 0 THEN 'Mass send sections skipped: CommunicationRecipient has no index on CommunicationId. Run the pending Rock Update Helper v18.1 job, or run this script in SSMS with @ReadWithoutIndex = 1.' END AS [skipped],
        CONVERT( CHAR(10), @Start12, 120 ) AS [periodStart],
        CONVERT( CHAR(10), @Start3, 120 ) AS [recipientPeriodStart],
        CONVERT( CHAR(10), @EndDate, 120 ) AS [periodEndExclusive]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

SELECT [s].[Section], [s].[Json]
FROM ( VALUES
    ( 1, 'meta', @JMeta ),
    ( 2, 'massSendSummary', @JMassSendSummary ),
    ( 3, 'massSendsByMedium', @JMassSendsByMedium ),
    ( 4, 'massSendSizeBands', @JMassSendSizeBands ),
    ( 5, 'massSendsByMonth', @JMassSendsByMonth ),
    ( 6, 'massSendsByWeekday', @JMassSendsByWeekday ),
    ( 7, 'massSendsByHourBand', @JMassSendsByHourBand ),
    ( 8, 'communicationLists', @JCommunicationLists ),
    ( 9, 'massOutcomes', @JMassOutcomes ),
    ( 10, 'massEngagement', @JMassEngagement ),
    ( 11, 'massLinks', @JMassLinks ),
    ( 12, 'massResponseDelay', @JMassResponseDelay ),
    ( 13, 'massUnsubscribes', @JMassUnsubscribes ),
    ( 14, 'communicationPeople', @JPeople ),
    ( 15, 'appDevices', @JAppDevices ),
    ( 16, 'smsInbound', @JSmsInbound ),
    ( 17, 'smsInboundByMonth', @JSmsInboundByMonth ),
    ( 18, 'smsInboundTiming', @JSmsInboundTiming ),
    ( 19, 'smsMassReplies', @JSmsMassReplies ),
    ( 20, 'smsThreads', @JSmsThreads ),
    ( 21, 'smsReplyDelay', @JSmsReplyDelay ),
    ( 22, 'smsOutboundLength', @JSmsOutboundLength ),
    ( 23, 'systemPhoneNumbers', @JSystemPhoneNumbers ),
    ( 24, 'communicationFlows', @JFlows )
) AS [s] ( [Order], [Section], [Json] )
ORDER BY [s].[Order];

DROP TABLE IF EXISTS #CommId, #Size, #Mass, #Sample, #SampleRecipient, #MassOutcome, #Component, #Int, #Adult, #Resp, #Texter, #SmsOutRaw, #SmsSend, #SmsOut, #Flow;

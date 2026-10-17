SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

-- =============================================
-- Description: Sample data calibration, part 3 of 3: registrations, prayer,
--              communications, and connections.
--
--              Collects anonymous, aggregate statistics from a live Rock
--              database. Returns no names, no Ids, and no personal data.
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
--              Use the same @ChurchLabel in all three parts.
--              Also runs in SSMS. Requires SQL Server 2016 or later.
-- Date: 2026-10-14
-- =============================================

-- Settings
DECLARE @ChurchLabel NVARCHAR(50) = 'Unlabeled'; -- Use a neutral label like source-2, never a church name.

-- The window is the last 24 complete months. The current month is excluded.
DECLARE @EndDate DATE = DATEFROMPARTS( YEAR( GETDATE() ), MONTH( GETDATE() ), 1 );
DECLARE @Start12 DATE = DATEADD( MONTH, -12, @EndDate );
DECLARE @Start24 DATE = DATEADD( MONTH, -24, @EndDate );

-- Clean up temp tables from an earlier run in the same SSMS session.
DROP TABLE IF EXISTS #Reg, #CommId, #Comm, #RecipientAgg, #ConnReq;

-- =============================================
-- Registrations (last 12 months)
-- =============================================

SELECT [r].[Id] AS [RegistrationId], [r].[RegistrationInstanceId]
INTO #Reg
FROM [Registration] AS [r]
WHERE [r].[IsTemporary] = 0
    AND [r].[CreatedDateTime] >= @Start12
    AND [r].[CreatedDateTime] < @EndDate;

DECLARE @JRegistrations NVARCHAR(MAX) = (
    SELECT [s].*, [pc].*
    FROM (
        SELECT
            COUNT( DISTINCT [r].[RegistrationInstanceId] ) AS [instancesWithRegistrations],
            COUNT( DISTINCT [r].[RegistrationId] ) AS [registrations],
            COUNT( [rr].[Id] ) AS [registrants],
            CAST( COUNT( [rr].[Id] ) * 1.0 / NULLIF( COUNT( DISTINCT [r].[RegistrationId] ), 0 ) AS DECIMAL(5,2) ) AS [avgRegistrantsPerRegistration],
            CAST( AVG( CASE WHEN [rr].[OnWaitList] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctWaitList],
            CAST( AVG( CASE WHEN [rr].[Cost] > 0 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctPaidRegistrants],
            CAST( AVG( CASE WHEN [p].[AgeClassification] = 2 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctChildRegistrants]
        FROM #Reg AS [r]
        LEFT JOIN [RegistrationRegistrant] AS [rr] ON [rr].[RegistrationId] = [r].[RegistrationId]
        LEFT JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [rr].[PersonAliasId]
        LEFT JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
    ) AS [s]
    OUTER APPLY (
        SELECT TOP 1
            PERCENTILE_CONT( 0.25 ) WITHIN GROUP ( ORDER BY [ic].[Registrants] ) OVER () AS [registrantsPerInstanceP25],
            PERCENTILE_CONT( 0.50 ) WITHIN GROUP ( ORDER BY [ic].[Registrants] ) OVER () AS [registrantsPerInstanceP50],
            PERCENTILE_CONT( 0.90 ) WITHIN GROUP ( ORDER BY [ic].[Registrants] ) OVER () AS [registrantsPerInstanceP90]
        FROM (
            SELECT [r].[RegistrationInstanceId], COUNT( [rr].[Id] ) AS [Registrants]
            FROM #Reg AS [r]
            LEFT JOIN [RegistrationRegistrant] AS [rr] ON [rr].[RegistrationId] = [r].[RegistrationId]
            GROUP BY [r].[RegistrationInstanceId]
        ) AS [ic]
    ) AS [pc]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

-- =============================================
-- Prayer (last 24 months)
-- =============================================

DECLARE @JPrayerSummary NVARCHAR(MAX) = (
    SELECT
        COUNT( * ) AS [requests],
        CAST( AVG( CASE WHEN [pr].[IsApproved] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctApproved],
        CAST( AVG( CASE WHEN [pr].[IsPublic] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctPublic],
        CAST( AVG( CASE WHEN [pr].[IsUrgent] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctUrgent],
        CAST( AVG( CASE WHEN [pr].[AllowComments] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctAllowComments],
        CAST( AVG( CASE WHEN ISNULL( [pr].[Answer], '' ) <> '' THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctAnswered],
        CAST( AVG( CASE WHEN [pr].[RequestedByPersonAliasId] IS NOT NULL THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctLinkedToPerson],
        CAST( AVG( CASE WHEN [pr].[CategoryId] IS NOT NULL THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctWithCategory],
        CAST( AVG( ISNULL( [pr].[PrayerCount], 0 ) * 1.0 ) AS DECIMAL(6,1) ) AS [avgPrayerCount],
        CAST( AVG( LEN( [pr].[Text] ) * 1.0 ) AS DECIMAL(7,0) ) AS [avgTextLength]
    FROM [PrayerRequest] AS [pr]
    WHERE [pr].[EnteredDateTime] >= @Start24 AND [pr].[EnteredDateTime] < @EndDate
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

DECLARE @JPrayerByMonth NVARCHAR(MAX) = (
    SELECT
        CONVERT( CHAR(7), [pr].[EnteredDateTime], 120 ) AS [month],
        COUNT( * ) AS [requests]
    FROM [PrayerRequest] AS [pr]
    WHERE [pr].[EnteredDateTime] >= @Start24 AND [pr].[EnteredDateTime] < @EndDate
    GROUP BY CONVERT( CHAR(7), [pr].[EnteredDateTime], 120 )
    ORDER BY CONVERT( CHAR(7), [pr].[EnteredDateTime], 120 )
    FOR JSON PATH
);

-- =============================================
-- Communications (last 12 months, sent only)
-- =============================================

-- Communication rows hold the full message body, so read the Ids from the
-- SendDateTime index first, then fetch only those rows by primary key.
SELECT [c].[Id] AS [CommunicationId]
INTO #CommId
FROM [Communication] AS [c]
WHERE [c].[SendDateTime] >= @Start12 AND [c].[SendDateTime] < @EndDate;

-- Communication type: 0 Recipient preference, 1 Email, 2 SMS, 3 Push.
SELECT
    [c].[Id] AS [CommunicationId],
    [c].[IsBulkCommunication],
    [c].[SendDateTime],
    CASE [c].[CommunicationType] WHEN 1 THEN 'Email' WHEN 2 THEN 'SMS' WHEN 3 THEN 'Push' ELSE 'Recipient preference' END AS [Medium]
INTO #Comm
FROM #CommId AS [ci]
INNER JOIN [Communication] AS [c] WITH ( FORCESEEK ) ON [c].[Id] = [ci].[CommunicationId];

CREATE CLUSTERED INDEX [IX_CommunicationId] ON #Comm ( [CommunicationId] );

/*
    10/14/26 - CLAUDE

    Each CommunicationRecipient row stores a copy of the sent message, so this
    is often the largest table in the database. This query uses only columns
    in the IX_CommunicationId index (CommunicationId, Status, OpenedDateTime)
    so SQL never reads the full rows. The medium comes from the communication,
    not the recipient, for the same reason. Only the last 3 months are read.

    Reason: Reading full recipient rows timed out on a large church.
*/
-- Recipient status: 0 Pending, 1 Delivered, 2 Failed, 3 Cancelled, 4 Opened, 5 Sending.
SELECT
    [c].[CommunicationId],
    [c].[Medium],
    COUNT( * ) AS [Recipients],
    SUM( CASE WHEN [cr].[Status] IN ( 1, 4 ) THEN 1 ELSE 0 END ) AS [Delivered],
    SUM( CASE WHEN [cr].[Status] = 4 OR [cr].[OpenedDateTime] IS NOT NULL THEN 1 ELSE 0 END ) AS [Opened],
    SUM( CASE WHEN [cr].[Status] = 2 THEN 1 ELSE 0 END ) AS [Failed],
    SUM( CASE WHEN [cr].[Status] = 3 THEN 1 ELSE 0 END ) AS [Cancelled]
INTO #RecipientAgg
FROM #Comm AS [c]
INNER JOIN [CommunicationRecipient] AS [cr] ON [cr].[CommunicationId] = [c].[CommunicationId]
WHERE [c].[SendDateTime] >= DATEADD( MONTH, -3, @EndDate )
GROUP BY [c].[CommunicationId], [c].[Medium];

DECLARE @JCommunicationsByMedium NVARCHAR(MAX) = (
    SELECT
        [ra].[Medium] AS [medium],
        COUNT( DISTINCT [ra].[CommunicationId] ) AS [communications],
        SUM( [ra].[Recipients] ) AS [recipients],
        CAST( SUM( [ra].[Delivered] ) * 100.0 / NULLIF( SUM( [ra].[Recipients] ), 0 ) AS DECIMAL(5,1) ) AS [pctDelivered],
        CAST( SUM( [ra].[Opened] ) * 100.0 / NULLIF( SUM( [ra].[Recipients] ), 0 ) AS DECIMAL(5,1) ) AS [pctOpened],
        CAST( SUM( [ra].[Failed] ) * 100.0 / NULLIF( SUM( [ra].[Recipients] ), 0 ) AS DECIMAL(5,1) ) AS [pctFailed],
        CAST( SUM( [ra].[Cancelled] ) * 100.0 / NULLIF( SUM( [ra].[Recipients] ), 0 ) AS DECIMAL(5,1) ) AS [pctCancelled]
    FROM #RecipientAgg AS [ra]
    GROUP BY [ra].[Medium]
    ORDER BY SUM( [ra].[Recipients] ) DESC
    FOR JSON PATH
);

DECLARE @JCommunicationSummary NVARCHAR(MAX) = (
    SELECT [s].*, [pc].*
    FROM (
        SELECT
            COUNT( * ) AS [communicationsSent],
            CAST( AVG( CASE WHEN [c].[IsBulkCommunication] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctBulk]
        FROM #Comm AS [c]
    ) AS [s]
    OUTER APPLY (
        SELECT TOP 1
            PERCENTILE_CONT( 0.50 ) WITHIN GROUP ( ORDER BY [t].[Recipients] ) OVER () AS [recipientsP50],
            PERCENTILE_CONT( 0.90 ) WITHIN GROUP ( ORDER BY [t].[Recipients] ) OVER () AS [recipientsP90],
            PERCENTILE_CONT( 0.99 ) WITHIN GROUP ( ORDER BY [t].[Recipients] ) OVER () AS [recipientsP99]
        FROM (
            SELECT [ra].[CommunicationId], SUM( [ra].[Recipients] ) AS [Recipients]
            FROM #RecipientAgg AS [ra]
            GROUP BY [ra].[CommunicationId]
        ) AS [t]
    ) AS [pc]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

DECLARE @JCommunicationsByMonth NVARCHAR(MAX) = (
    SELECT
        CONVERT( CHAR(7), [c].[SendDateTime], 120 ) AS [month],
        COUNT( * ) AS [communications]
    FROM #Comm AS [c]
    GROUP BY CONVERT( CHAR(7), [c].[SendDateTime], 120 )
    ORDER BY CONVERT( CHAR(7), [c].[SendDateTime], 120 )
    FOR JSON PATH
);

-- =============================================
-- Connections (last 24 months)
-- =============================================

SELECT
    [cr].[ConnectionOpportunityId],
    [cr].[ConnectionState],
    [cr].[ConnectorPersonAliasId],
    [cr].[CampusId],
    [cr].[CreatedDateTime],
    [cr].[ConnectedDateTime]
INTO #ConnReq
FROM [ConnectionRequest] AS [cr]
WHERE [cr].[CreatedDateTime] >= @Start24 AND [cr].[CreatedDateTime] < @EndDate;

-- Connection state: 0 Active, 1 Inactive, 2 Future Follow Up, 3 Connected.
DECLARE @JConnections NVARCHAR(MAX) = (
    SELECT [s].*, [ct].*, [op].*
    FROM (
        SELECT
            COUNT( * ) AS [requests],
            COUNT( DISTINCT [c].[ConnectionOpportunityId] ) AS [opportunitiesWithRequests],
            CAST( AVG( CASE WHEN [c].[ConnectionState] = 0 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctActive],
            CAST( AVG( CASE WHEN [c].[ConnectionState] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctInactive],
            CAST( AVG( CASE WHEN [c].[ConnectionState] = 2 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctFutureFollowUp],
            CAST( AVG( CASE WHEN [c].[ConnectionState] = 3 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctConnected],
            CAST( AVG( CASE WHEN [c].[ConnectorPersonAliasId] IS NOT NULL THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctWithConnector],
            CAST( AVG( CASE WHEN [c].[CampusId] IS NOT NULL THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctWithCampus]
        FROM #ConnReq AS [c]
    ) AS [s]
    OUTER APPLY (
        SELECT TOP 1
            PERCENTILE_CONT( 0.50 ) WITHIN GROUP ( ORDER BY DATEDIFF( DAY, [c].[CreatedDateTime], [c].[ConnectedDateTime] ) ) OVER () AS [daysToConnectP50],
            PERCENTILE_CONT( 0.90 ) WITHIN GROUP ( ORDER BY DATEDIFF( DAY, [c].[CreatedDateTime], [c].[ConnectedDateTime] ) ) OVER () AS [daysToConnectP90]
        FROM #ConnReq AS [c]
        WHERE [c].[ConnectedDateTime] IS NOT NULL
    ) AS [ct]
    OUTER APPLY (
        SELECT TOP 1
            PERCENTILE_CONT( 0.50 ) WITHIN GROUP ( ORDER BY [o].[Requests] ) OVER () AS [requestsPerOpportunityP50],
            PERCENTILE_CONT( 0.90 ) WITHIN GROUP ( ORDER BY [o].[Requests] ) OVER () AS [requestsPerOpportunityP90]
        FROM (
            SELECT [c].[ConnectionOpportunityId], COUNT( * ) AS [Requests]
            FROM #ConnReq AS [c]
            GROUP BY [c].[ConnectionOpportunityId]
        ) AS [o]
    ) AS [op]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

DECLARE @JConnectionsByMonth NVARCHAR(MAX) = (
    SELECT
        CONVERT( CHAR(7), [c].[CreatedDateTime], 120 ) AS [month],
        COUNT( * ) AS [requests]
    FROM #ConnReq AS [c]
    GROUP BY CONVERT( CHAR(7), [c].[CreatedDateTime], 120 )
    ORDER BY CONVERT( CHAR(7), [c].[CreatedDateTime], 120 )
    FOR JSON PATH
);

-- =============================================
-- Output: one row per section. Rock's SQL editor shows only this result.
-- =============================================

DECLARE @JMeta NVARCHAR(MAX) = (
    SELECT
        @ChurchLabel AS [church],
        3 AS [part],
        CONVERT( CHAR(10), @Start24, 120 ) AS [periodStart],
        CONVERT( CHAR(10), @EndDate, 120 ) AS [periodEndExclusive]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

SELECT [s].[Section], [s].[Json]
FROM ( VALUES
    ( 1, 'meta', @JMeta ),
    ( 2, 'registrations', @JRegistrations ),
    ( 3, 'prayerSummary', @JPrayerSummary ),
    ( 4, 'prayerByMonth', @JPrayerByMonth ),
    ( 5, 'communicationSummaryRecipientsLast3Months', @JCommunicationSummary ),
    ( 6, 'communicationsByMediumLast3Months', @JCommunicationsByMedium ),
    ( 7, 'communicationsByMonth', @JCommunicationsByMonth ),
    ( 8, 'connections', @JConnections ),
    ( 9, 'connectionsByMonth', @JConnectionsByMonth )
) AS [s] ( [Order], [Section], [Json] )
ORDER BY [s].[Order];

DROP TABLE IF EXISTS #Reg, #CommId, #Comm, #RecipientAgg, #ConnReq;

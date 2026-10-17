SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

-- =============================================
-- Description: Sample data calibration, part 2 of 3: attendance, check-in,
--              groups, serving, and how features overlap.
--
--              Collects anonymous, aggregate statistics from a live Rock
--              database. Returns no names, no Ids, and no personal data.
--              Group type names are included. Edit out any that contain the
--              church's name before sharing.
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
DECLARE @Start26Weeks DATE = DATEADD( WEEK, -26, @EndDate );

-- Lookups
DECLARE @ContributionTypeValueId INT = ( SELECT TOP 1 [Id] FROM [DefinedValue] WHERE [Guid] = '2D607262-52D6-4724-910D-5C6E8FB89ACC' );
DECLARE @PersonRecordTypeValueId INT = ( SELECT TOP 1 [Id] FROM [DefinedValue] WHERE [Guid] = '36CF10D6-C695-413D-8E7C-4546EFEF385E' );
DECLARE @ActiveRecordStatusValueId INT = ( SELECT TOP 1 [Id] FROM [DefinedValue] WHERE [Guid] = '618F906C-C33D-4FA3-8AEF-E58CB7B63F1E' );

-- Clean up temp tables from an earlier run in the same SSMS session.
DROP TABLE IF EXISTS #ExcludedGroupType, #Person, #GivingUnit12, #Att, #ActiveMember, #Attended12, #InGroup, #Serving, #GroupSize, #GroupTypeMedian;

-- Group types that are not real "groups" (families, security roles, relationships).
CREATE TABLE #ExcludedGroupType ( [Id] INT PRIMARY KEY );
INSERT INTO #ExcludedGroupType ( [Id] )
SELECT [gt].[Id]
FROM [GroupType] AS [gt]
WHERE [gt].[Guid] IN (
    '790E3215-3B10-442B-AF69-616C0DCB998E', -- Family
    'AECE949F-704C-483E-A4FB-93D5E4720C4C', -- Security Role
    'E0C5A0E2-B7B3-4EF4-820D-BBF7F9A374EF', -- Known Relationships
    '8C0E5852-F08F-4327-9AA5-87800A6AB53E'  -- Peer Network
);

-- =============================================
-- Base sets
-- =============================================

-- Active, living people (not businesses).
SELECT
    [p].[Id] AS [PersonId],
    [p].[GivingId],
    [p].[AgeClassification],
    [p].[ConnectionStatusValueId],
    [p].[CreatedDateTime]
INTO #Person
FROM [Person] AS [p]
WHERE [p].[RecordTypeValueId] = @PersonRecordTypeValueId
    AND [p].[RecordStatusValueId] = @ActiveRecordStatusValueId
    AND [p].[IsDeceased] = 0;

CREATE CLUSTERED INDEX [IX_PersonId] ON #Person ( [PersonId] );

-- Giving units with at least one gift in the last 12 months.
SELECT DISTINCT [gp].[GivingId]
INTO #GivingUnit12
FROM [FinancialTransaction] AS [ft]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [ft].[AuthorizedPersonAliasId]
INNER JOIN [Person] AS [gp] ON [gp].[Id] = [pa].[PersonId]
WHERE [ft].[TransactionTypeValueId] = @ContributionTypeValueId
    AND [ft].[TransactionDateTime] >= @Start12
    AND [ft].[TransactionDateTime] < @EndDate
    AND EXISTS ( SELECT 1 FROM [FinancialTransactionDetail] AS [ftd] WHERE [ftd].[TransactionId] = [ft].[Id] AND [ftd].[Amount] > 0 );

-- Attendance in the 24 month window, including did-not-attend rows.
-- A record with a device or attendance code came from check-in.
SELECT
    [pa].[PersonId],
    [p].[AgeClassification],
    [p].[PrimaryFamilyId],
    [ao].[SundayDate],
    [a].[StartDateTime],
    [g].[GroupTypeId],
    [a].[DidAttend],
    CASE WHEN [a].[DeviceId] IS NOT NULL OR [a].[AttendanceCodeId] IS NOT NULL THEN 1 ELSE 0 END AS [IsCheckIn],
    CASE WHEN [a].[IsFirstTime] = 1 THEN 1 ELSE 0 END AS [IsFirstTime]
INTO #Att
FROM [Attendance] AS [a]
INNER JOIN [AttendanceOccurrence] AS [ao] ON [ao].[Id] = [a].[OccurrenceId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [a].[PersonAliasId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
LEFT JOIN [Group] AS [g] ON [g].[Id] = [ao].[GroupId]
WHERE [a].[StartDateTime] >= @Start24
    AND [a].[StartDateTime] < @EndDate;

CREATE CLUSTERED INDEX [IX_PersonId] ON #Att ( [PersonId] );

-- Active members of active, real groups.
SELECT
    [gm].[GroupId],
    [gm].[GroupTypeId],
    [gm].[PersonId],
    CASE WHEN [gtr].[IsLeader] = 1 THEN 1 ELSE 0 END AS [IsLeader]
INTO #ActiveMember
FROM [GroupMember] AS [gm]
INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
INNER JOIN [GroupTypeRole] AS [gtr] ON [gtr].[Id] = [gm].[GroupRoleId]
WHERE [gm].[GroupMemberStatus] = 1
    AND [gm].[IsArchived] = 0
    AND [g].[IsActive] = 1
    AND [g].[IsArchived] = 0
    AND [gm].[GroupTypeId] NOT IN ( SELECT [Id] FROM #ExcludedGroupType );

CREATE CLUSTERED INDEX [IX_PersonId] ON #ActiveMember ( [PersonId] );

CREATE CLUSTERED INDEX [IX_GivingId] ON #GivingUnit12 ( [GivingId] );

-- Per-person flags, built once so the engagement queries stay fast.
SELECT DISTINCT [a].[PersonId]
INTO #Attended12
FROM #Att AS [a]
WHERE [a].[DidAttend] = 1 AND [a].[StartDateTime] >= @Start12;

CREATE CLUSTERED INDEX [IX_PersonId] ON #Attended12 ( [PersonId] );

SELECT DISTINCT [am].[PersonId]
INTO #InGroup
FROM #ActiveMember AS [am]
INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [am].[GroupTypeId]
WHERE [gt].[IsSchedulingEnabled] = 0 AND [gt].[TakesAttendance] = 1;

CREATE CLUSTERED INDEX [IX_PersonId] ON #InGroup ( [PersonId] );

SELECT DISTINCT [am].[PersonId]
INTO #Serving
FROM #ActiveMember AS [am]
INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [am].[GroupTypeId]
WHERE [gt].[IsSchedulingEnabled] = 1;

CREATE CLUSTERED INDEX [IX_PersonId] ON #Serving ( [PersonId] );

-- =============================================
-- Attendance and check-in
-- =============================================

DECLARE @JAttendanceWeekly NVARCHAR(MAX) = (
    SELECT
        CONVERT( CHAR(10), [a].[SundayDate], 120 ) AS [weekEnding],
        COUNT( DISTINCT CASE WHEN [a].[IsCheckIn] = 1 AND [a].[AgeClassification] = 2 THEN [a].[PersonId] END ) AS [checkInChildren],
        COUNT( DISTINCT CASE WHEN [a].[IsCheckIn] = 1 AND [a].[AgeClassification] <> 2 THEN [a].[PersonId] END ) AS [checkInAdults],
        COUNT( DISTINCT CASE WHEN [a].[IsCheckIn] = 0 THEN [a].[PersonId] END ) AS [nonCheckInAttendees],
        SUM( CASE WHEN [a].[IsCheckIn] = 1 THEN [a].[IsFirstTime] ELSE 0 END ) AS [firstTimeCheckIns]
    FROM #Att AS [a]
    WHERE [a].[DidAttend] = 1
    GROUP BY [a].[SundayDate]
    ORDER BY [a].[SundayDate]
    FOR JSON PATH
);

-- How many of the last 26 weeks each checked-in person attended.
DECLARE @JCheckInFrequency NVARCHAR(MAX) = (
    SELECT
        [w].[AgeGroup] AS [ageGroup],
        [w].[WeeksBucket] AS [weeksAttendedOf26],
        COUNT( * ) AS [people]
    FROM (
        SELECT
            CASE WHEN MAX( [a].[AgeClassification] ) = 2 THEN 'Child' ELSE 'Adult or unknown' END AS [AgeGroup],
            CASE
                WHEN COUNT( DISTINCT [a].[SundayDate] ) = 1 THEN '01'
                WHEN COUNT( DISTINCT [a].[SundayDate] ) <= 3 THEN '02-03'
                WHEN COUNT( DISTINCT [a].[SundayDate] ) <= 7 THEN '04-07'
                WHEN COUNT( DISTINCT [a].[SundayDate] ) <= 12 THEN '08-12'
                WHEN COUNT( DISTINCT [a].[SundayDate] ) <= 19 THEN '13-19'
                ELSE '20-26'
            END AS [WeeksBucket]
        FROM #Att AS [a]
        WHERE [a].[DidAttend] = 1
            AND [a].[IsCheckIn] = 1
            AND [a].[StartDateTime] >= @Start26Weeks
        GROUP BY [a].[PersonId]
    ) AS [w]
    GROUP BY [w].[AgeGroup], [w].[WeeksBucket]
    ORDER BY [w].[AgeGroup], [w].[WeeksBucket]
    FOR JSON PATH
);

-- Check-in times (0 = Sunday). Only slots with at least 0.1% of check-ins.
-- 1900-01-07 was a Sunday, so this ignores DATEFIRST.
DECLARE @JCheckInTimes NVARCHAR(MAX) = (
    SELECT [t].*
    FROM (
        SELECT
            DATEDIFF( DAY, '19000107', [a].[StartDateTime] ) % 7 AS [dayOfWeek],
            DATEPART( HOUR, [a].[StartDateTime] ) AS [hour],
            CAST( COUNT( * ) * 100.0 / SUM( COUNT( * ) ) OVER () AS DECIMAL(5,1) ) AS [pctCheckIns]
        FROM #Att AS [a]
        WHERE [a].[DidAttend] = 1 AND [a].[IsCheckIn] = 1 AND [a].[StartDateTime] >= @Start12
        GROUP BY DATEDIFF( DAY, '19000107', [a].[StartDateTime] ) % 7, DATEPART( HOUR, [a].[StartDateTime] )
    ) AS [t]
    WHERE [t].[pctCheckIns] >= 0.1
    ORDER BY [t].[dayOfWeek], [t].[hour]
    FOR JSON PATH
);

-- When a family checks kids in, how many kids come together.
DECLARE @JKidsPerFamilyCheckIn NVARCHAR(MAX) = (
    SELECT
        [k].[Kids] AS [kidsCheckedIn],
        CAST( COUNT( * ) * 100.0 / SUM( COUNT( * ) ) OVER () AS DECIMAL(5,1) ) AS [pctFamilyWeeks]
    FROM (
        SELECT CASE WHEN COUNT( DISTINCT [a].[PersonId] ) >= 4 THEN '4+' ELSE CAST( COUNT( DISTINCT [a].[PersonId] ) AS VARCHAR(2) ) END AS [Kids]
        FROM #Att AS [a]
        WHERE [a].[DidAttend] = 1
            AND [a].[IsCheckIn] = 1
            AND [a].[AgeClassification] = 2
            AND [a].[PrimaryFamilyId] IS NOT NULL
            AND [a].[StartDateTime] >= @Start12
        GROUP BY [a].[PrimaryFamilyId], [a].[SundayDate]
    ) AS [k]
    GROUP BY [k].[Kids]
    ORDER BY [k].[Kids]
    FOR JSON PATH
);

-- =============================================
-- Groups and serving
-- =============================================

SELECT [am].[GroupId], [am].[GroupTypeId], COUNT( * ) AS [Members]
INTO #GroupSize
FROM #ActiveMember AS [am]
GROUP BY [am].[GroupId], [am].[GroupTypeId];

SELECT DISTINCT
    [gs].[GroupTypeId],
    PERCENTILE_CONT( 0.5 ) WITHIN GROUP ( ORDER BY [gs].[Members] ) OVER ( PARTITION BY [gs].[GroupTypeId] ) AS [MedianGroupSize]
INTO #GroupTypeMedian
FROM #GroupSize AS [gs];

DECLARE @JGroupTypes NVARCHAR(MAX) = (
    SELECT TOP 30
        [gt].[Name] AS [groupType],
        ISNULL( [pv].[Value], '' ) AS [purpose],
        [gt].[TakesAttendance] AS [takesAttendance],
        [gt].[IsSchedulingEnabled] AS [isSchedulingEnabled],
        [s].[ActiveGroups] AS [activeGroups],
        [s].[ActiveMembers] AS [activeMembers],
        [s].[DistinctPeople] AS [distinctPeople],
        [s].[PctLeaders] AS [pctLeaders],
        CAST( [m].[MedianGroupSize] AS DECIMAL(6,1) ) AS [medianGroupSize],
        ISNULL( [ar].[Records], 0 ) AS [attendanceRecordsLast12],
        [ar].[PctDidAttend] AS [pctDidAttend]
    FROM [GroupType] AS [gt]
    INNER JOIN (
        SELECT
            [am].[GroupTypeId],
            COUNT( DISTINCT [am].[GroupId] ) AS [ActiveGroups],
            COUNT( * ) AS [ActiveMembers],
            COUNT( DISTINCT [am].[PersonId] ) AS [DistinctPeople],
            CAST( AVG( [am].[IsLeader] * 100.0 ) AS DECIMAL(5,1) ) AS [PctLeaders]
        FROM #ActiveMember AS [am]
        GROUP BY [am].[GroupTypeId]
    ) AS [s] ON [s].[GroupTypeId] = [gt].[Id]
    LEFT JOIN #GroupTypeMedian AS [m] ON [m].[GroupTypeId] = [gt].[Id]
    LEFT JOIN [DefinedValue] AS [pv] ON [pv].[Id] = [gt].[GroupTypePurposeValueId]
    LEFT JOIN (
        SELECT
            [a].[GroupTypeId],
            COUNT( * ) AS [Records],
            CAST( AVG( CASE WHEN [a].[DidAttend] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [PctDidAttend]
        FROM #Att AS [a]
        WHERE [a].[StartDateTime] >= @Start12 AND [a].[DidAttend] IS NOT NULL
        GROUP BY [a].[GroupTypeId]
    ) AS [ar] ON [ar].[GroupTypeId] = [gt].[Id]
    ORDER BY [s].[ActiveMembers] DESC
    FOR JSON PATH
);

DECLARE @JGroupsPerAdult NVARCHAR(MAX) = (
    SELECT
        [c].[Groups] AS [activeGroups],
        COUNT( * ) AS [adults]
    FROM (
        SELECT
            CASE
                WHEN COUNT( DISTINCT [am].[GroupId] ) >= 3 THEN '3+'
                ELSE CAST( COUNT( DISTINCT [am].[GroupId] ) AS VARCHAR(2) )
            END AS [Groups]
        FROM #Person AS [p]
        LEFT JOIN #ActiveMember AS [am] ON [am].[PersonId] = [p].[PersonId]
        WHERE [p].[AgeClassification] = 1
        GROUP BY [p].[PersonId]
    ) AS [c]
    GROUP BY [c].[Groups]
    ORDER BY [c].[Groups]
    FOR JSON PATH
);

-- =============================================
-- Cross-feature engagement (active adults, last 12 months)
-- This is the most important section: it shows how features overlap.
-- =============================================

DECLARE @JEngagement NVARCHAR(MAX) = (
    SELECT
        [e].[ConnectionStatus] AS [connectionStatus],
        [e].[Gives] AS [gives],
        [e].[Attends] AS [attends],
        [e].[InGroup] AS [inGroup],
        [e].[Serves] AS [serves],
        COUNT( * ) AS [adults]
    FROM (
        SELECT
            ISNULL( [dv].[Value], 'None' ) AS [ConnectionStatus],
            CASE WHEN [gu].[GivingId] IS NOT NULL THEN 1 ELSE 0 END AS [Gives],
            CASE WHEN [at].[PersonId] IS NOT NULL THEN 1 ELSE 0 END AS [Attends],
            CASE WHEN [ig].[PersonId] IS NOT NULL THEN 1 ELSE 0 END AS [InGroup],
            CASE WHEN [sv].[PersonId] IS NOT NULL THEN 1 ELSE 0 END AS [Serves]
        FROM #Person AS [p]
        LEFT JOIN [DefinedValue] AS [dv] ON [dv].[Id] = [p].[ConnectionStatusValueId]
        LEFT JOIN #GivingUnit12 AS [gu] ON [gu].[GivingId] = [p].[GivingId]
        LEFT JOIN #Attended12 AS [at] ON [at].[PersonId] = [p].[PersonId]
        LEFT JOIN #InGroup AS [ig] ON [ig].[PersonId] = [p].[PersonId]
        LEFT JOIN #Serving AS [sv] ON [sv].[PersonId] = [p].[PersonId]
        WHERE [p].[AgeClassification] = 1
    ) AS [e]
    GROUP BY [e].[ConnectionStatus], [e].[Gives], [e].[Attends], [e].[InGroup], [e].[Serves]
    ORDER BY [e].[ConnectionStatus], [e].[Gives] DESC, [e].[Attends] DESC, [e].[InGroup] DESC, [e].[Serves] DESC
    FOR JSON PATH
);

-- Adults added 13-24 months ago: how many are still active, and where are they now?
DECLARE @JNewPeopleOutcome NVARCHAR(MAX) = (
    SELECT
        ( SELECT COUNT( * ) FROM [Person] AS [ap] WHERE [ap].[RecordTypeValueId] = @PersonRecordTypeValueId AND [ap].[AgeClassification] = 1 AND [ap].[CreatedDateTime] >= @Start24 AND [ap].[CreatedDateTime] < @Start12 ) AS [adultsAdded],
        COUNT( * ) AS [stillActive],
        CAST( AVG( [n].[Gave] * 100.0 ) AS DECIMAL(5,1) ) AS [pctGaveLast12],
        CAST( AVG( [n].[Attended] * 100.0 ) AS DECIMAL(5,1) ) AS [pctAttendedLast12],
        CAST( AVG( [n].[InAnyGroup] * 100.0 ) AS DECIMAL(5,1) ) AS [pctInAnyGroup]
    FROM (
        SELECT
            CASE WHEN [gu].[GivingId] IS NOT NULL THEN 1 ELSE 0 END AS [Gave],
            CASE WHEN [at].[PersonId] IS NOT NULL THEN 1 ELSE 0 END AS [Attended],
            CASE WHEN EXISTS ( SELECT 1 FROM #ActiveMember AS [am] WHERE [am].[PersonId] = [p].[PersonId] ) THEN 1 ELSE 0 END AS [InAnyGroup]
        FROM #Person AS [p]
        LEFT JOIN #GivingUnit12 AS [gu] ON [gu].[GivingId] = [p].[GivingId]
        LEFT JOIN #Attended12 AS [at] ON [at].[PersonId] = [p].[PersonId]
        WHERE [p].[AgeClassification] = 1
            AND [p].[CreatedDateTime] >= @Start24
            AND [p].[CreatedDateTime] < @Start12
    ) AS [n]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

-- =============================================
-- Output: one row per section. Rock's SQL editor shows only this result.
-- =============================================

DECLARE @JMeta NVARCHAR(MAX) = (
    SELECT
        @ChurchLabel AS [church],
        2 AS [part],
        CONVERT( CHAR(10), @Start24, 120 ) AS [periodStart],
        CONVERT( CHAR(10), @EndDate, 120 ) AS [periodEndExclusive]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

SELECT [s].[Section], [s].[Json]
FROM ( VALUES
    ( 1, 'meta', @JMeta ),
    ( 2, 'attendanceWeekly', @JAttendanceWeekly ),
    ( 3, 'checkInFrequency', @JCheckInFrequency ),
    ( 4, 'checkInTimes', @JCheckInTimes ),
    ( 5, 'kidsPerFamilyCheckIn', @JKidsPerFamilyCheckIn ),
    ( 6, 'groupTypes', @JGroupTypes ),
    ( 7, 'groupsPerAdult', @JGroupsPerAdult ),
    ( 8, 'engagement', @JEngagement ),
    ( 9, 'newPeopleOutcome', @JNewPeopleOutcome )
) AS [s] ( [Order], [Section], [Json] )
ORDER BY [s].[Order];

DROP TABLE IF EXISTS #ExcludedGroupType, #Person, #GivingUnit12, #Att, #ActiveMember, #Attended12, #InGroup, #Serving, #GroupSize, #GroupTypeMedian;

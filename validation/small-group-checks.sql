-- Checks a loaded database against the small group rules in configuration/small-groups.json.
-- Each numbered result says what to expect. Run it with sqlcmd -i or in any SQL editor.

SET NOCOUNT ON;
DECLARE @SmallGroupTypeId INT = ( SELECT [Id] FROM [GroupType] WHERE [Guid] = '50FCFB30-F51A-49DF-86F4-2B176EA1820B' );
DECLARE @LeaderRoleId INT = ( SELECT [Id] FROM [GroupTypeRole] WHERE [Guid] = '6D798EFA-0110-41D5-BCE4-30ACEFE4317E' );

PRINT '1. Groups by state and campus';
SELECT [c].[Name] AS [Campus],
    SUM( CASE WHEN [g].[IsActive] = 1 AND [g].[IsArchived] = 0 THEN 1 ELSE 0 END ) AS [Active],
    SUM( CASE WHEN [g].[IsActive] = 0 AND [g].[IsArchived] = 0 THEN 1 ELSE 0 END ) AS [Inactive],
    SUM( CASE WHEN [g].[IsArchived] = 1 THEN 1 ELSE 0 END ) AS [Archived]
FROM [Group] AS [g] INNER JOIN [Campus] AS [c] ON [c].[Id] = [g].[CampusId]
WHERE [g].[GroupTypeId] = @SmallGroupTypeId GROUP BY [c].[Name] ORDER BY [c].[Name];

PRINT '2. Leaders: count, how many are not male or under 18 (expect 0), youngest';
SELECT COUNT( * ) AS [Leaders], SUM( CASE WHEN [p].[Gender] <> 1 OR [p].[Age] < 18 THEN 1 ELSE 0 END ) AS [NotMaleOrUnder18], MIN( [p].[Age] ) AS [YoungestLeader]
FROM [GroupMember] AS [gm] INNER JOIN [Person] AS [p] ON [p].[Id] = [gm].[PersonId]
WHERE [gm].[GroupTypeId] = @SmallGroupTypeId AND [gm].[GroupRoleId] = @LeaderRoleId;

PRINT '3. Members under 18 or without an age (expect 0), youngest member';
SELECT SUM( CASE WHEN [p].[Age] < 18 OR [p].[Age] IS NULL THEN 1 ELSE 0 END ) AS [Under18], MIN( [p].[Age] ) AS [Youngest], COUNT( * ) AS [Memberships]
FROM [GroupMember] AS [gm] INNER JOIN [Person] AS [p] ON [p].[Id] = [gm].[PersonId]
WHERE [gm].[GroupTypeId] = @SmallGroupTypeId;

PRINT '4. Active group sizes (active members)';
SELECT COUNT( * ) AS [ActiveGroups], MIN( [s].[Members] ) AS [Smallest], MAX( [s].[Members] ) AS [Largest], CAST( AVG( [s].[Members] * 1.0 ) AS DECIMAL(5,1) ) AS [Average]
FROM ( SELECT [g].[Id], COUNT( [gm].[Id] ) AS [Members]
       FROM [Group] AS [g] LEFT JOIN [GroupMember] AS [gm] ON [gm].[GroupId] = [g].[Id] AND [gm].[GroupMemberStatus] = 1 AND [gm].[IsArchived] = 0
       WHERE [g].[GroupTypeId] = @SmallGroupTypeId AND [g].[IsActive] = 1 AND [g].[IsArchived] = 0 GROUP BY [g].[Id] ) AS [s];

PRINT '5. Schedules: day and time';
SELECT DATENAME( WEEKDAY, DATEADD( DAY, [s].[WeeklyDayOfWeek], '19000107' ) ) AS [Day], CONVERT( VARCHAR(5), [s].[WeeklyTimeOfDay], 108 ) AS [Time], COUNT( * ) AS [Groups]
FROM [Group] AS [g] INNER JOIN [Schedule] AS [s] ON [s].[Id] = [g].[ScheduleId]
WHERE [g].[GroupTypeId] = @SmallGroupTypeId GROUP BY [s].[WeeklyDayOfWeek], [s].[WeeklyTimeOfDay] ORDER BY [s].[WeeklyDayOfWeek], [s].[WeeklyTimeOfDay];

PRINT '6. Meeting style and child care (all small groups)';
SELECT [a].[Name] AS [Attribute], [av].[Value], COUNT( * ) AS [Groups], CAST( COUNT( * ) * 100.0 / SUM( COUNT( * ) ) OVER ( PARTITION BY [a].[Name] ) AS DECIMAL(5,1) ) AS [Pct]
FROM [AttributeValue] AS [av] INNER JOIN [Attribute] AS [a] ON [a].[Id] = [av].[AttributeId]
INNER JOIN [Group] AS [g] ON [g].[Id] = [av].[EntityId] AND [g].[GroupTypeId] = @SmallGroupTypeId
WHERE [a].[Guid] IN ( 'CA3A7E3E-E19D-43EB-BC79-EC56B2620A1A', 'F2288750-3F38-4451-ACA9-2C9B8C739458' )
GROUP BY [a].[Name], [av].[Value] ORDER BY [a].[Name], [av].[Value];

PRINT '7. In-person groups without a meeting location, online groups with one or without a link (expect 0)';
SELECT SUM( CASE WHEN [style].[Value] = 'In Person' AND [gl].[Id] IS NULL THEN 1 ELSE 0 END ) AS [InPersonWithoutLocation],
    SUM( CASE WHEN [style].[Value] = 'Online' AND ( [gl].[Id] IS NOT NULL OR [link].[Value] IS NULL ) THEN 1 ELSE 0 END ) AS [OnlineWithLocationOrNoLink]
FROM [Group] AS [g]
INNER JOIN [AttributeValue] AS [style] ON [style].[EntityId] = [g].[Id] AND [style].[AttributeId] = ( SELECT [Id] FROM [Attribute] WHERE [Guid] = 'CA3A7E3E-E19D-43EB-BC79-EC56B2620A1A' )
LEFT JOIN [AttributeValue] AS [link] ON [link].[EntityId] = [g].[Id] AND [link].[AttributeId] = ( SELECT [Id] FROM [Attribute] WHERE [Guid] = '71782C30-BC89-439F-92F3-5445EB068732' )
LEFT JOIN [GroupLocation] AS [gl] ON [gl].[GroupId] = [g].[Id]
WHERE [g].[GroupTypeId] = @SmallGroupTypeId;

PRINT '8. Test cases: full (capacity = active members), closed (not public), pending (has pending members), inactive, archived';
SELECT
    SUM( CASE WHEN [g].[IsActive] = 1 AND [g].[IsArchived] = 0 AND [g].[GroupCapacity] = [m].[Active] THEN 1 ELSE 0 END ) AS [Full],
    SUM( CASE WHEN [g].[IsActive] = 1 AND [g].[IsArchived] = 0 AND [g].[IsPublic] = 0 THEN 1 ELSE 0 END ) AS [Closed],
    SUM( CASE WHEN [g].[IsActive] = 1 AND [g].[IsArchived] = 0 AND [m].[Pending] > 0 THEN 1 ELSE 0 END ) AS [Pending],
    SUM( CASE WHEN [g].[IsActive] = 0 AND [g].[IsArchived] = 0 THEN 1 ELSE 0 END ) AS [Inactive],
    SUM( CASE WHEN [g].[IsArchived] = 1 THEN 1 ELSE 0 END ) AS [Archived]
FROM [Group] AS [g]
CROSS APPLY ( SELECT SUM( CASE WHEN [gm].[GroupMemberStatus] = 1 AND [gm].[IsArchived] = 0 THEN 1 ELSE 0 END ) AS [Active], SUM( CASE WHEN [gm].[GroupMemberStatus] = 2 THEN 1 ELSE 0 END ) AS [Pending]
              FROM [GroupMember] AS [gm] WHERE [gm].[GroupId] = [g].[Id] ) AS [m]
WHERE [g].[GroupTypeId] = @SmallGroupTypeId;

PRINT '9. Groups, members, and group locations without exactly one current history row (expect 0 each), and history row counts';
SELECT
    ( SELECT COUNT( * ) FROM [Group] AS [g] WHERE [g].[GroupTypeId] = @SmallGroupTypeId AND ( SELECT COUNT( * ) FROM [GroupHistorical] AS [h] WHERE [h].[GroupId] = [g].[Id] AND [h].[CurrentRowIndicator] = 1 ) <> 1 ) AS [Groups],
    ( SELECT COUNT( * ) FROM [GroupMember] AS [gm] WHERE [gm].[GroupTypeId] = @SmallGroupTypeId AND ( SELECT COUNT( * ) FROM [GroupMemberHistorical] AS [h] WHERE [h].[GroupMemberId] = [gm].[Id] AND [h].[CurrentRowIndicator] = 1 ) <> 1 ) AS [Members],
    ( SELECT COUNT( * ) FROM [GroupLocation] AS [gl] INNER JOIN [Group] AS [g] ON [g].[Id] = [gl].[GroupId] WHERE [g].[GroupTypeId] = @SmallGroupTypeId AND ( SELECT COUNT( * ) FROM [GroupLocationHistorical] AS [h] WHERE [h].[GroupLocationId] = [gl].[Id] AND [h].[CurrentRowIndicator] = 1 ) <> 1 ) AS [GroupLocations],
    ( SELECT COUNT( * ) FROM [GroupHistorical] AS [h] INNER JOIN [Group] AS [g] ON [g].[Id] = [h].[GroupId] WHERE [g].[GroupTypeId] = @SmallGroupTypeId ) AS [GroupHistoryRows],
    ( SELECT COUNT( * ) FROM [GroupMemberHistorical] AS [h] INNER JOIN [Group] AS [g] ON [g].[Id] = [h].[GroupId] WHERE [g].[GroupTypeId] = @SmallGroupTypeId ) AS [MemberHistoryRows],
    ( SELECT COUNT( * ) FROM [GroupLocationHistorical] AS [h] INNER JOIN [Group] AS [g] ON [g].[Id] = [h].[GroupId] WHERE [g].[GroupTypeId] = @SmallGroupTypeId ) AS [LocationHistoryRows];

PRINT '10. Did-not-meet occurrences by week (SundayDate)';
SELECT CONVERT( CHAR(10), [ao].[SundayDate], 120 ) AS [WeekEnding], COUNT( * ) AS [Occurrences]
FROM [AttendanceOccurrence] AS [ao] INNER JOIN [Group] AS [g] ON [g].[Id] = [ao].[GroupId]
WHERE [g].[GroupTypeId] = @SmallGroupTypeId AND [ao].[DidNotOccur] = 1
GROUP BY [ao].[SundayDate] ORDER BY [ao].[SundayDate];

PRINT '11. Attendance for pending members, before joining, after leaving, after the group ended (expect 0)';
SELECT
    SUM( CASE WHEN [gm].[GroupMemberStatus] = 2 THEN 1 ELSE 0 END ) AS [PendingAttendance],
    SUM( CASE WHEN [ao].[OccurrenceDate] < CAST( [gm].[DateTimeAdded] AS DATE ) THEN 1 ELSE 0 END ) AS [BeforeJoined],
    SUM( CASE WHEN [gm].[InactiveDateTime] IS NOT NULL AND [ao].[OccurrenceDate] >= CAST( [gm].[InactiveDateTime] AS DATE ) THEN 1 ELSE 0 END ) AS [AfterLeft],
    SUM( CASE WHEN [g].[InactiveDateTime] IS NOT NULL AND [ao].[OccurrenceDate] >= CAST( [g].[InactiveDateTime] AS DATE ) THEN 1 ELSE 0 END ) AS [AfterGroupEnded],
    COUNT( * ) AS [Rows]
FROM [Attendance] AS [a]
INNER JOIN [AttendanceOccurrence] AS [ao] ON [ao].[Id] = [a].[OccurrenceId]
INNER JOIN [Group] AS [g] ON [g].[Id] = [ao].[GroupId] AND [g].[GroupTypeId] = @SmallGroupTypeId
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [a].[PersonAliasId]
INNER JOIN [GroupMember] AS [gm] ON [gm].[GroupId] = [g].[Id] AND [gm].[PersonId] = [pa].[PersonId];

PRINT '12. Group attendance by month, all years combined';
SELECT MONTH( [ao].[OccurrenceDate] ) AS [Month], SUM( CASE WHEN [a].[DidAttend] = 1 THEN 1 ELSE 0 END ) AS [Attended], COUNT( * ) AS [Rows], COUNT( DISTINCT [ao].[Id] ) AS [Meetings],
    CAST( AVG( CASE WHEN [a].[DidAttend] = 1 THEN 100.0 ELSE 0 END ) AS DECIMAL(5,1) ) AS [PctAttended]
FROM [Attendance] AS [a]
INNER JOIN [AttendanceOccurrence] AS [ao] ON [ao].[Id] = [a].[OccurrenceId]
INNER JOIN [Group] AS [g] ON [g].[Id] = [ao].[GroupId] AND [g].[GroupTypeId] = @SmallGroupTypeId
GROUP BY MONTH( [ao].[OccurrenceDate] ) ORDER BY [Month];

PRINT '13. Latest December day with a meeting that took place';
SELECT MAX( DAY( [ao].[OccurrenceDate] ) ) AS [LastDecemberMeetingDay]
FROM [AttendanceOccurrence] AS [ao] INNER JOIN [Group] AS [g] ON [g].[Id] = [ao].[GroupId] AND [g].[GroupTypeId] = @SmallGroupTypeId
WHERE MONTH( [ao].[OccurrenceDate] ) = 12 AND [ao].[DidNotOccur] = 0;

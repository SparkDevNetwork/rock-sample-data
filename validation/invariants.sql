/*
    Invariants: rules that must hold in every loaded dataset, on every Rock version.

    The loader bulk-copies around Rock's save hooks, so Rock never gets to enforce these itself. The verify tool
    (verify/README.md) runs each check after a load and fails when any returns more than zero. Each check starts
    with "-- check: name", says why it matters on the next "-- why:" line, and returns one number: how many rows
    break the rule. @ForeignKey is the dataset's key, for example SampleData:core-1.

    Rules that a foreign key or unique index already enforces are left out: the bulk copy checks those.
    "Every family has an adult" is left out on purpose: kids whose parents are not in Rock, and Jenny Greggs's
    memorial record, are child-only families, as they are in real churches.
*/

-- check: dataset-has-people
-- why: Every rule below looks only at this dataset's rows. A load that wrote nothing would pass them all.
SELECT CASE WHEN EXISTS ( SELECT 1 FROM [Person] WHERE [ForeignKey] = @ForeignKey ) THEN 0 ELSE 1 END;

-- check: person-has-one-primary-alias
-- why: Rock finds a person through their primary alias; every audit column and gift points at it.
SELECT COUNT(*)
FROM [Person] AS [p]
WHERE [p].[ForeignKey] = @ForeignKey
    AND ( [p].[PrimaryAliasId] IS NULL
        OR NOT EXISTS ( SELECT 1 FROM [PersonAlias] AS [pa] WHERE [pa].[Id] = [p].[PrimaryAliasId] AND [pa].[PersonId] = [p].[Id] AND [pa].[AliasPersonId] = [p].[Id] )
        OR ( SELECT COUNT(*) FROM [PersonAlias] AS [pa] WHERE [pa].[PersonId] = [p].[Id] AND [pa].[AliasPersonId] = [p].[Id] ) <> 1 );

-- check: person-is-in-a-family
-- why: Rock expects every person to belong to at least one family; profile pages and check-in break without one.
SELECT COUNT(*)
FROM [Person] AS [p]
WHERE [p].[ForeignKey] = @ForeignKey
    AND NOT EXISTS (
        SELECT 1
        FROM [GroupMember] AS [gm]
        INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
        INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
        WHERE [gm].[PersonId] = [p].[Id] AND [gt].[Guid] = '790E3215-3B10-442B-AF69-616C0DCB998E' );

-- check: primary-family-is-a-family-the-person-is-in
-- why: Rock's save hook sets the primary family and campus; the loader fills them in itself.
SELECT COUNT(*)
FROM [Person] AS [p]
WHERE [p].[ForeignKey] = @ForeignKey
    AND ( [p].[PrimaryFamilyId] IS NULL
        OR NOT EXISTS ( SELECT 1 FROM [GroupMember] AS [gm] WHERE [gm].[GroupId] = [p].[PrimaryFamilyId] AND [gm].[PersonId] = [p].[Id] ) );

-- check: primary-campus-is-the-primary-family-campus
-- why: Rock shows and filters people by the campus of their primary family.
SELECT COUNT(*)
FROM [Person] AS [p]
INNER JOIN [Group] AS [f] ON [f].[Id] = [p].[PrimaryFamilyId]
WHERE [p].[ForeignKey] = @ForeignKey
    AND ISNULL( [p].[PrimaryCampusId], -1 ) <> ISNULL( [f].[CampusId], -1 );

-- check: giving-id-matches-giving-group
-- why: Rock groups gifts by GivingId (G + giving group, or P + person). A wrong one splits or merges giving units.
SELECT COUNT(*)
FROM [Person] AS [p]
WHERE [p].[ForeignKey] = @ForeignKey
    AND ( ISNULL( [p].[GivingId], '' ) <> CASE WHEN [p].[GivingGroupId] IS NULL THEN 'P' + CAST( [p].[Id] AS VARCHAR(20) ) ELSE 'G' + CAST( [p].[GivingGroupId] AS VARCHAR(20) ) END
        OR [p].[GivingLeaderId] IS NULL );

-- check: giving-leader-is-in-the-giving-unit
-- why: The giving leader receives the statement; it must be the person or an adult of their giving group.
SELECT COUNT(*)
FROM [Person] AS [p]
WHERE [p].[ForeignKey] = @ForeignKey
    AND [p].[GivingLeaderId] <> [p].[Id]
    AND NOT EXISTS ( SELECT 1 FROM [GroupMember] AS [gm] WHERE [gm].[GroupId] = [p].[GivingGroupId] AND [gm].[PersonId] = [p].[GivingLeaderId] );

-- check: birth-date-parts-agree
-- why: Rock stores the birth date three ways (date, day/month/year, and a sortable key); reports read all three.
SELECT COUNT(*)
FROM [Person] AS [p]
WHERE [p].[ForeignKey] = @ForeignKey
    AND [p].[BirthDate] IS NOT NULL
    AND ( [p].[BirthYear] <> YEAR( [p].[BirthDate] ) OR [p].[BirthMonth] <> MONTH( [p].[BirthDate] ) OR [p].[BirthDay] <> DAY( [p].[BirthDate] )
        OR [p].[BirthDateKey] <> YEAR( [p].[BirthDate] ) * 10000 + MONTH( [p].[BirthDate] ) * 100 + DAY( [p].[BirthDate] ) );

-- check: phones-use-the-reserved-range
-- why: Content standard 2. Phones are 555-0100 to 555-0199 in a Phoenix-area code, so no real person gets a call.
SELECT COUNT(*)
FROM [PhoneNumber] AS [pn]
INNER JOIN [Person] AS [p] ON [p].[Id] = [pn].[PersonId]
WHERE [p].[ForeignKey] = @ForeignKey
    AND ( LEN( [pn].[Number] ) <> 10 OR LEFT( [pn].[Number], 3 ) NOT IN ( '602', '480', '623' ) OR SUBSTRING( [pn].[Number], 4, 5 ) <> '55501' );

-- check: emails-use-reserved-domains
-- why: Content standard 3. Emails use example.org, or rocksolidchurchdemo.com for the sample church's staff.
SELECT COUNT(*)
FROM [Person] AS [p]
WHERE [p].[ForeignKey] = @ForeignKey
    AND ISNULL( [p].[Email], '' ) <> ''
    AND [p].[Email] NOT LIKE '%@example.org'
    AND [p].[Email] NOT LIKE '%@rocksolidchurchdemo.com';

-- check: no-race-or-ethnicity
-- why: Content standard 8. Photo ethnicity only makes matching family photos; it never goes into Rock.
SELECT COUNT(*)
FROM [Person] AS [p]
WHERE [p].[ForeignKey] = @ForeignKey
    AND ( [p].[RaceValueId] IS NOT NULL OR [p].[EthnicityValueId] IS NOT NULL );

-- check: home-addresses-are-mapped
-- why: Group Finder, campus distance, and maps need a point for every home address.
SELECT COUNT(*)
FROM [GroupLocation] AS [gl]
INNER JOIN [Group] AS [g] ON [g].[Id] = [gl].[GroupId]
INNER JOIN [Location] AS [l] ON [l].[Id] = [gl].[LocationId]
INNER JOIN [DefinedValue] AS [t] ON [t].[Id] = [gl].[GroupLocationTypeValueId]
WHERE [g].[ForeignKey] = @ForeignKey
    AND [t].[Guid] = '8C52E53C-2A66-435A-AE6E-5EE307D9A0DC'
    AND [l].[GeoPoint] IS NULL;

-- check: nothing-dated-in-the-future
-- why: The timeline counts back from the install day. A future gift or check-in shows up in "upcoming" places.
--      Requests to serve in the weeks ahead are the one exception: they are scheduled and not attended (decision 74).
SELECT ( SELECT COUNT(*) FROM [Attendance] WHERE [ForeignKey] = @ForeignKey AND [StartDateTime] >= DATEADD( DAY, 1, CAST( GETDATE() AS DATE ) ) AND NOT ( [RequestedToAttend] = 1 AND [DidAttend] = 0 ) )
    + ( SELECT COUNT(*) FROM [FinancialTransaction] WHERE [ForeignKey] = @ForeignKey AND [TransactionDateTime] >= DATEADD( DAY, 1, CAST( GETDATE() AS DATE ) ) );

-- check: transaction-has-positive-details
-- why: Rock totals a gift from its details. A gift with no detail, or a zero or negative one, breaks statements.
SELECT COUNT(*)
FROM [FinancialTransaction] AS [t]
WHERE [t].[ForeignKey] = @ForeignKey
    AND ( NOT EXISTS ( SELECT 1 FROM [FinancialTransactionDetail] AS [d] WHERE [d].[TransactionId] = [t].[Id] )
        OR EXISTS ( SELECT 1 FROM [FinancialTransactionDetail] AS [d] WHERE [d].[TransactionId] = [t].[Id] AND [d].[Amount] <= 0 ) );

-- check: transaction-is-inside-its-batch
-- why: Batches cover a date range. A gift outside its batch's range lands in the wrong batch report.
SELECT COUNT(*)
FROM [FinancialTransaction] AS [t]
INNER JOIN [FinancialBatch] AS [b] ON [b].[Id] = [t].[BatchId]
WHERE [t].[ForeignKey] = @ForeignKey
    AND ( [t].[TransactionDateTime] < [b].[BatchStartDateTime] OR [t].[TransactionDateTime] >= [b].[BatchEndDateTime] );

-- check: member-role-belongs-to-the-group-type
-- why: Rock only lets a member hold a role of their group's type. The database does not enforce it.
SELECT COUNT(*)
FROM [GroupMember] AS [gm]
INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
INNER JOIN [GroupTypeRole] AS [r] ON [r].[Id] = [gm].[GroupRoleId]
WHERE [gm].[ForeignKey] = @ForeignKey
    AND [r].[GroupTypeId] <> [g].[GroupTypeId];

-- check: one-current-history-row
-- why: Rock's history tables mark exactly one current row per group, member, and meeting location.
SELECT ( SELECT COUNT(*) FROM ( SELECT [GroupId] FROM [GroupHistorical] WHERE [ForeignKey] = @ForeignKey GROUP BY [GroupId] HAVING SUM( CASE WHEN [CurrentRowIndicator] = 1 THEN 1 ELSE 0 END ) <> 1 ) AS [x] )
    + ( SELECT COUNT(*) FROM ( SELECT [GroupMemberId] FROM [GroupMemberHistorical] WHERE [ForeignKey] = @ForeignKey GROUP BY [GroupMemberId] HAVING SUM( CASE WHEN [CurrentRowIndicator] = 1 THEN 1 ELSE 0 END ) <> 1 ) AS [x] )
    + ( SELECT COUNT(*) FROM ( SELECT [GroupLocationId] FROM [GroupLocationHistorical] WHERE [ForeignKey] = @ForeignKey GROUP BY [GroupLocationId] HAVING SUM( CASE WHEN [CurrentRowIndicator] = 1 THEN 1 ELSE 0 END ) <> 1 ) AS [x] );

-- check: photo-is-a-stored-person-image
-- why: A photo id must point at a Person Image file whose bytes are in the database, or the profile shows a broken image.
SELECT COUNT(*)
FROM [Person] AS [p]
LEFT JOIN [BinaryFile] AS [f] ON [f].[Id] = [p].[PhotoId]
LEFT JOIN [BinaryFileType] AS [ft] ON [ft].[Id] = [f].[BinaryFileTypeId]
WHERE [p].[ForeignKey] = @ForeignKey
    AND [p].[PhotoId] IS NOT NULL
    AND ( [f].[Id] IS NULL
        OR [ft].[Guid] <> '03BD8476-8A9F-4078-B628-5B538F967AFC'
        OR NOT EXISTS ( SELECT 1 FROM [BinaryFileData] AS [d] WHERE [d].[Id] = [f].[Id] AND DATALENGTH( [d].[Content] ) > 0 ) );

-- check: only-this-dataset
-- why: A database holds one dataset. Rows from another would mean a load on a database that was not fresh.
SELECT ( SELECT COUNT(*) FROM [Person] WHERE [ForeignKey] LIKE 'SampleData:%' AND [ForeignKey] <> @ForeignKey )
    + ( SELECT COUNT(*) FROM [Group] WHERE [ForeignKey] LIKE 'SampleData:%' AND [ForeignKey] <> @ForeignKey )
    + ( SELECT COUNT(*) FROM [FinancialTransaction] WHERE [ForeignKey] LIKE 'SampleData:%' AND [ForeignKey] <> @ForeignKey );

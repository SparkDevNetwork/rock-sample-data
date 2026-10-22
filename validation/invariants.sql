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

-- check: note-is-on-a-person-of-this-dataset
-- why: A person note's EntityId must point at a loaded person, or the note is an orphan Rock never shows.
SELECT COUNT(*)
FROM [Note] AS [n]
INNER JOIN [NoteType] AS [nt] ON [nt].[Id] = [n].[NoteTypeId]
INNER JOIN [EntityType] AS [et] ON [et].[Id] = [nt].[EntityTypeId] AND [et].[Name] = 'Rock.Model.Person'
WHERE [n].[ForeignKey] = @ForeignKey
    AND NOT EXISTS ( SELECT 1 FROM [Person] AS [p] WHERE [p].[Id] = [n].[EntityId] AND [p].[ForeignKey] = @ForeignKey );

-- check: note-reply-matches-its-parent
-- why: Rock shows a reply under its parent, so it must be on the same entity, of the same type, and written after it.
SELECT COUNT(*)
FROM [Note] AS [r]
INNER JOIN [Note] AS [n] ON [n].[Id] = [r].[ParentNoteId]
WHERE [r].[ForeignKey] = @ForeignKey
    AND ( [r].[EntityId] <> [n].[EntityId] OR [r].[NoteTypeId] <> [n].[NoteTypeId] OR [r].[CreatedDateTime] < [n].[CreatedDateTime] );

-- check: note-author-is-not-the-noted-person
-- why: Staff write notes about other people; a note about its own author looks like a load error.
SELECT COUNT(*)
FROM [Note] AS [n]
INNER JOIN [NoteType] AS [nt] ON [nt].[Id] = [n].[NoteTypeId]
INNER JOIN [EntityType] AS [et] ON [et].[Id] = [nt].[EntityTypeId] AND [et].[Name] = 'Rock.Model.Person'
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [n].[CreatedByPersonAliasId]
WHERE [n].[ForeignKey] = @ForeignKey
    AND [pa].[PersonId] = [n].[EntityId];

-- check: person-note-looks-saved-by-rock
-- why: Rock's save hook approves notes whose type needs no approval, and its editor sets EditedDateTime and a private note's caption. Unsent flags would make Rock's Send Note Notifications job email staff about old notes.
SELECT COUNT(*)
FROM [Note] AS [n]
INNER JOIN [NoteType] AS [nt] ON [nt].[Id] = [n].[NoteTypeId]
INNER JOIN [EntityType] AS [et] ON [et].[Id] = [nt].[EntityTypeId] AND [et].[Name] = 'Rock.Model.Person'
WHERE [n].[ForeignKey] = @ForeignKey
    AND ( [n].[ApprovalStatus] <> 1
        OR [n].[NotificationsSent] = 0
        OR [n].[ApprovalsSent] = 0
        OR [n].[EditedDateTime] IS NULL
        OR [n].[CreatedByPersonAliasId] IS NULL
        OR ( [n].[IsPrivateNote] = 1 AND ISNULL( [n].[Caption], '' ) <> 'You - Personal Note' ) );

-- check: step-matches-its-membership
-- why: A Small Group, Serve, or Lead step records one membership (decision 86): same person, group, and dates, In Progress only while the member is Pending. And every membership that counts has its step.
WITH [Memberships] AS (
    SELECT [p].[PrimaryAliasId], [gm].[GroupId], [gt].[Guid] AS [GroupTypeGuid], [r].[IsLeader], [gm].[GroupMemberStatus], [gm].[DateTimeAdded],
        CASE WHEN [gm].[GroupMemberStatus] = 0 OR [gm].[IsArchived] = 1 THEN ISNULL( [gm].[InactiveDateTime], [gm].[ArchivedDateTime] ) END AS [MemberEnded],
        CASE WHEN [g].[IsActive] = 0 OR [g].[IsArchived] = 1 THEN ISNULL( [g].[InactiveDateTime], [g].[ArchivedDateTime] ) END AS [GroupEnded]
    FROM [GroupMember] AS [gm]
    INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
    INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
    INNER JOIN [GroupTypeRole] AS [r] ON [r].[Id] = [gm].[GroupRoleId]
    INNER JOIN [Person] AS [p] ON [p].[Id] = [gm].[PersonId]
    WHERE [gm].[ForeignKey] = @ForeignKey
        AND [gt].[Guid] IN ( '50FCFB30-F51A-49DF-86F4-2B176EA1820B', '2C42B2D4-1C5F-4AD5-A9AD-08631B872AC4' )
),
[Expected] AS (
    SELECT [PrimaryAliasId], [GroupId], CAST( 'EFA15A4F-5666-4153-B92F-AF3ECD73C504' AS UNIQUEIDENTIFIER ) AS [StepTypeGuid], [DateTimeAdded], [MemberEnded], [GroupEnded], 0 AS [IsInProgress]
    FROM [Memberships] WHERE [GroupTypeGuid] = '50FCFB30-F51A-49DF-86F4-2B176EA1820B' AND [GroupMemberStatus] <> 2
    UNION ALL
    SELECT [PrimaryAliasId], [GroupId], CAST( '71E66730-8F7D-4EEF-9C53-524C4BDE5E59' AS UNIQUEIDENTIFIER ), [DateTimeAdded], [MemberEnded], [GroupEnded], CASE WHEN [GroupMemberStatus] = 2 THEN 1 ELSE 0 END
    FROM [Memberships] WHERE [GroupTypeGuid] = '2C42B2D4-1C5F-4AD5-A9AD-08631B872AC4'
    UNION ALL
    SELECT [PrimaryAliasId], [GroupId], CAST( 'CFF0E432-3AB6-4290-8F6E-C1A3D8D8F375' AS UNIQUEIDENTIFIER ), [DateTimeAdded], [MemberEnded], [GroupEnded], 0
    FROM [Memberships] WHERE [IsLeader] = 1 AND [GroupMemberStatus] <> 2
),
[ExpectedSteps] AS (
    SELECT [PrimaryAliasId], [GroupId], [StepTypeGuid], [DateTimeAdded], [IsInProgress],
        CASE WHEN [MemberEnded] IS NULL THEN [GroupEnded] WHEN [GroupEnded] IS NULL OR [MemberEnded] < [GroupEnded] THEN [MemberEnded] ELSE [GroupEnded] END AS [Ended]
    FROM [Expected]
),
[Steps] AS (
    SELECT [s].[PersonAliasId], [s].[RelatedEntityId], [st].[Guid] AS [StepTypeGuid], [s].[StartDateTime], [s].[EndDateTime], CASE WHEN [ss].[IsCompleteStatus] = 1 THEN 0 ELSE 1 END AS [IsInProgress]
    FROM [Step] AS [s]
    INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
    INNER JOIN [StepStatus] AS [ss] ON [ss].[Id] = [s].[StepStatusId]
    WHERE [s].[ForeignKey] = @ForeignKey
        AND [st].[Guid] IN ( 'EFA15A4F-5666-4153-B92F-AF3ECD73C504', '71E66730-8F7D-4EEF-9C53-524C4BDE5E59', 'CFF0E432-3AB6-4290-8F6E-C1A3D8D8F375' )
)
SELECT ( SELECT COUNT(*) FROM [Steps] AS [s] WHERE NOT EXISTS (
            SELECT 1 FROM [ExpectedSteps] AS [e]
            WHERE [e].[PrimaryAliasId] = [s].[PersonAliasId] AND [e].[GroupId] = [s].[RelatedEntityId] AND [e].[StepTypeGuid] = [s].[StepTypeGuid]
                AND [e].[DateTimeAdded] = [s].[StartDateTime] AND ISNULL( [e].[Ended], '9999-01-01' ) = ISNULL( [s].[EndDateTime], '9999-01-01' ) AND [e].[IsInProgress] = [s].[IsInProgress] ) )
    + ( SELECT COUNT(*) FROM [ExpectedSteps] AS [e] WHERE NOT EXISTS (
            SELECT 1 FROM [Steps] AS [s]
            WHERE [e].[PrimaryAliasId] = [s].[PersonAliasId] AND [e].[GroupId] = [s].[RelatedEntityId] AND [e].[StepTypeGuid] = [s].[StepTypeGuid] AND [e].[DateTimeAdded] = [s].[StartDateTime] ) );

-- check: step-looks-saved-by-rock
-- why: Rock's step model fills the date keys from the dates, a Complete step has a completion date and an In Progress one has none, and the sample data puts every step on the person's primary alias and campus.
SELECT COUNT(*)
FROM [Step] AS [s]
INNER JOIN [StepStatus] AS [ss] ON [ss].[Id] = [s].[StepStatusId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [s].[PersonAliasId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
WHERE [s].[ForeignKey] = @ForeignKey
    AND ( ISNULL( [s].[StartDateKey], 0 ) <> ISNULL( CONVERT( INT, FORMAT( [s].[StartDateTime], 'yyyyMMdd' ) ), 0 )
        OR ISNULL( [s].[EndDateKey], 0 ) <> ISNULL( CONVERT( INT, FORMAT( [s].[EndDateTime], 'yyyyMMdd' ) ), 0 )
        OR ISNULL( [s].[CompletedDateKey], 0 ) <> ISNULL( CONVERT( INT, FORMAT( [s].[CompletedDateTime], 'yyyyMMdd' ) ), 0 )
        OR ( [ss].[IsCompleteStatus] = 1 AND [s].[CompletedDateTime] IS NULL )
        OR ( [ss].[IsCompleteStatus] = 0 AND [s].[CompletedDateTime] IS NOT NULL )
        OR [s].[StartDateTime] IS NULL
        OR ISNULL( [s].[CampusId], 0 ) <> ISNULL( [p].[PrimaryCampusId], 0 )
        OR [s].[PersonAliasId] <> [p].[PrimaryAliasId] );

-- check: baptism-step-agrees-with-baptism-date
-- why: The Baptism step and Rock's Baptism Date person attribute always agree, and the step's Baptized Here matches the person's.
WITH [Dates] AS (
    SELECT [av].[EntityId] AS [PersonId], [av].[ValueAsDateTime]
    FROM [AttributeValue] AS [av]
    INNER JOIN [Attribute] AS [a] ON [a].[Id] = [av].[AttributeId]
    INNER JOIN [Person] AS [p] ON [p].[Id] = [av].[EntityId]
    WHERE [a].[Guid] = 'D42763FA-28E9-4A55-A25A-48998D7D7FEF' AND [p].[ForeignKey] = @ForeignKey AND ISNULL( [av].[Value], '' ) <> ''
),
[Baptisms] AS (
    SELECT [pa].[PersonId], [s].[Id], [s].[StartDateTime], [s].[CompletedDateTime]
    FROM [Step] AS [s]
    INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
    INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [s].[PersonAliasId]
    WHERE [st].[Guid] = '801CC43C-0641-4271-939E-75E428F31D06' AND [s].[ForeignKey] = @ForeignKey
),
[Here] AS (
    SELECT [b].[Id],
        ( SELECT [av].[Value] FROM [AttributeValue] AS [av] INNER JOIN [Attribute] AS [a] ON [a].[Id] = [av].[AttributeId] WHERE [a].[Guid] = '343A557E-92CC-4A75-B251-0DA732F5EB4E' AND [av].[EntityId] = [b].[Id] ) AS [OnStep],
        ( SELECT [av].[Value] FROM [AttributeValue] AS [av] INNER JOIN [Attribute] AS [a] ON [a].[Id] = [av].[AttributeId] WHERE [a].[Guid] = '87986F92-4AC3-49D2-9753-328588F3BB7A' AND [av].[EntityId] = [b].[PersonId] ) AS [OnPerson]
    FROM [Baptisms] AS [b]
)
SELECT ( SELECT COUNT(*) FROM [Dates] AS [d] WHERE NOT EXISTS ( SELECT 1 FROM [Baptisms] AS [b] WHERE [b].[PersonId] = [d].[PersonId] AND [b].[StartDateTime] = [d].[ValueAsDateTime] AND [b].[CompletedDateTime] = [d].[ValueAsDateTime] ) )
    + ( SELECT COUNT(*) FROM [Baptisms] AS [b] WHERE NOT EXISTS ( SELECT 1 FROM [Dates] AS [d] WHERE [d].[PersonId] = [b].[PersonId] AND [d].[ValueAsDateTime] = [b].[StartDateTime] ) )
    + ( SELECT COUNT(*) FROM [Here] WHERE [OnStep] IS NULL OR [OnPerson] IS NULL OR [OnStep] <> [OnPerson] );

-- check: serve-step-follows-starting-point
-- why: Rock's one Discipleship Path prerequisite: Serve needs a completed Starting Point Class first (decision 84). Every volunteer has one before their Serve step starts.
SELECT COUNT(*)
FROM [Step] AS [s]
INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
WHERE [st].[Guid] = '71E66730-8F7D-4EEF-9C53-524C4BDE5E59' AND [s].[ForeignKey] = @ForeignKey
    AND NOT EXISTS (
        SELECT 1
        FROM [Step] AS [c]
        INNER JOIN [StepType] AS [ct] ON [ct].[Id] = [c].[StepTypeId]
        INNER JOIN [StepStatus] AS [cs] ON [cs].[Id] = [c].[StepStatusId]
        WHERE [ct].[Guid] = 'F3585131-3EBF-49C4-83A0-3F88271A9B9B' AND [c].[PersonAliasId] = [s].[PersonAliasId]
            AND [cs].[IsCompleteStatus] = 1 AND [c].[CompletedDateTime] < [s].[StartDateTime] );

-- check: give-step-agrees-with-journey-stage
-- why: A Give step runs from the week a giver becomes Consistent to the week they become Lapsed or Former (decision 85). So a giver Rock shows as Consistent has an open Give step, and an open Give step belongs to a Consistent or Occasional giver.
WITH [Stages] AS (
    SELECT [p].[PrimaryAliasId], [av].[Value] AS [Stage]
    FROM [Person] AS [p]
    INNER JOIN [AttributeValue] AS [av] ON [av].[EntityId] = [p].[Id]
    INNER JOIN [Attribute] AS [a] ON [a].[Id] = [av].[AttributeId]
    WHERE [a].[Guid] = '13C55AEA-6D88-4470-B3AE-EE5138F044DF' AND [p].[ForeignKey] = @ForeignKey
        AND [p].[RecordTypeValueId] <> ( SELECT [Id] FROM [DefinedValue] WHERE [Guid] = 'BF64ADD3-E70A-44CE-9C4B-E76BBED37550' )
),
[OpenGive] AS (
    SELECT DISTINCT [s].[PersonAliasId]
    FROM [Step] AS [s]
    INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
    WHERE [st].[Guid] = 'CE640CD3-DD6E-4BF3-ADDD-953F086E6E14' AND [s].[ForeignKey] = @ForeignKey AND [s].[EndDateTime] IS NULL
)
SELECT ( SELECT COUNT(*) FROM [Stages] AS [x] WHERE [x].[Stage] = '2' AND NOT EXISTS ( SELECT 1 FROM [OpenGive] AS [o] WHERE [o].[PersonAliasId] = [x].[PrimaryAliasId] ) )
    + ( SELECT COUNT(*) FROM [OpenGive] AS [o] WHERE NOT EXISTS ( SELECT 1 FROM [Stages] AS [x] WHERE [x].[PrimaryAliasId] = [o].[PersonAliasId] AND [x].[Stage] IN ( '2', '3' ) ) );

-- check: program-completions-follow-rocks-rule
-- why: Rock's Update Step Program Completions job records a completion for each person whose Complete steps cover every active step type, dated from those steps, and points every step of theirs in the program at it. Rock's job must find nothing to add or fix after install.
WITH [Program] AS (
    SELECT [Id] FROM [StepProgram] WHERE [Guid] = 'A31AE259-885E-4ACE-B8B6-56000F58EA3B'
),
[PeopleWithAllSteps] AS (
    SELECT [s].[PersonAliasId],
        MIN( COALESCE( [s].[StartDateTime], [s].[CreatedDateTime] ) ) AS [StartDateTime],
        MAX( COALESCE( [s].[CompletedDateTime], [s].[EndDateTime] ) ) AS [EndDateTime],
        MAX( [s].[CampusId] ) AS [CampusId]
    FROM [Step] AS [s]
    INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
    INNER JOIN [StepStatus] AS [ss] ON [ss].[Id] = [s].[StepStatusId] AND [ss].[IsCompleteStatus] = 1
    WHERE [st].[StepProgramId] = ( SELECT [Id] FROM [Program] ) AND [s].[ForeignKey] = @ForeignKey
    GROUP BY [s].[PersonAliasId]
    HAVING COUNT( DISTINCT [s].[StepTypeId] ) = ( SELECT COUNT(*) FROM [StepType] WHERE [StepProgramId] = ( SELECT [Id] FROM [Program] ) AND [IsActive] = 1 )
),
[Completions] AS (
    SELECT [c].[Id], [c].[PersonAliasId], [c].[StartDateTime], [c].[EndDateTime], [c].[CampusId], [c].[StartDateKey]
    FROM [StepProgramCompletion] AS [c]
    WHERE [c].[StepProgramId] = ( SELECT [Id] FROM [Program] ) AND [c].[ForeignKey] = @ForeignKey
)
SELECT ( SELECT COUNT(*) FROM [PeopleWithAllSteps] AS [w] WHERE NOT EXISTS (
            SELECT 1 FROM [Completions] AS [c]
            WHERE [c].[PersonAliasId] = [w].[PersonAliasId] AND [c].[StartDateTime] = [w].[StartDateTime] AND ISNULL( [c].[EndDateTime], '9999-01-01' ) = ISNULL( [w].[EndDateTime], '9999-01-01' )
                AND ISNULL( [c].[CampusId], 0 ) = ISNULL( [w].[CampusId], 0 ) AND [c].[StartDateKey] = CONVERT( INT, FORMAT( [w].[StartDateTime], 'yyyyMMdd' ) ) ) )
    + ( SELECT COUNT(*) FROM [Completions] AS [c] WHERE NOT EXISTS ( SELECT 1 FROM [PeopleWithAllSteps] AS [w] WHERE [w].[PersonAliasId] = [c].[PersonAliasId] ) )
    + ( SELECT COUNT(*) FROM ( SELECT [PersonAliasId] FROM [Completions] GROUP BY [PersonAliasId] HAVING COUNT(*) > 1 ) AS [x] )
    + ( SELECT COUNT(*)
        FROM [Step] AS [s]
        INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
        LEFT JOIN [Completions] AS [c] ON [c].[PersonAliasId] = [s].[PersonAliasId]
        WHERE [st].[StepProgramId] = ( SELECT [Id] FROM [Program] ) AND [s].[ForeignKey] = @ForeignKey
            AND ISNULL( [s].[StepProgramCompletionId], 0 ) <> ISNULL( [c].[Id], 0 ) );

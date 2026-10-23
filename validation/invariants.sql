/*
    Invariants: rules that must hold in every loaded dataset, on every Rock version.

    The loader bulk-copies around Rock's save hooks, so Rock never gets to enforce these itself. The verify tool
    (verify/README.md) runs each check after a load and fails when any returns more than zero. Each check starts
    with "-- check: name", says why it matters on the next "-- why:" line, and returns one number: how many rows
    break the rule. @ForeignKey is the dataset's key, for example SampleData:core-1. @CardPeople and @CardHouseholds
    are the dataset card's numbers from datasets/{key}.json, or null when the load used another seed than the
    default, which the card was not measured at.

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
-- why: The timeline counts back from the install day. A future gift, check-in, or step shows up in "upcoming" places.
--      Requests to serve in the weeks ahead are the one exception: they are scheduled and not attended (decision 74).
SELECT ( SELECT COUNT(*) FROM [Attendance] WHERE [ForeignKey] = @ForeignKey AND [StartDateTime] >= DATEADD( DAY, 1, CAST( GETDATE() AS DATE ) ) AND NOT ( [RequestedToAttend] = 1 AND [DidAttend] = 0 ) )
    + ( SELECT COUNT(*) FROM [FinancialTransaction] WHERE [ForeignKey] = @ForeignKey AND [TransactionDateTime] >= DATEADD( DAY, 1, CAST( GETDATE() AS DATE ) ) )
    + ( SELECT COUNT(*) FROM [Step] WHERE [ForeignKey] = @ForeignKey
            AND ( [StartDateTime] >= DATEADD( DAY, 1, CAST( GETDATE() AS DATE ) ) OR [EndDateTime] >= DATEADD( DAY, 1, CAST( GETDATE() AS DATE ) ) OR [CompletedDateTime] >= DATEADD( DAY, 1, CAST( GETDATE() AS DATE ) ) ) )
    + ( SELECT COUNT(*) FROM [StepProgramCompletion] WHERE [ForeignKey] = @ForeignKey AND [EndDateTime] >= DATEADD( DAY, 1, CAST( GETDATE() AS DATE ) ) );

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

-- check: step-has-its-person-history
-- why: Rock's step save hook writes person history: one "Added" row per step on the day it was made, and a status change on the day a step that started In Progress was completed. The person's History tab shows each step.
WITH [StepHistory] AS (
    SELECT [h].*
    FROM [History] AS [h]
    INNER JOIN [Category] AS [c] ON [c].[Id] = [h].[CategoryId]
    WHERE [h].[ForeignKey] = @ForeignKey AND [c].[Guid] = '517BB2D3-0A50-4132-818E-63BB3C81EAE9'
)
SELECT ( SELECT COUNT(*)
        FROM [Step] AS [s]
        INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
        INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [s].[PersonAliasId]
        WHERE [s].[ForeignKey] = @ForeignKey
            AND ( SELECT COUNT(*) FROM [StepHistory] AS [h]
                WHERE [h].[RelatedEntityId] = [s].[Id] AND [h].[EntityId] = [pa].[PersonId] AND [h].[Verb] = 'STEPADDED'
                    AND [h].[ValueName] = [st].[Name] AND [h].[CreatedDateTime] = [s].[StartDateTime] ) <> 1 )
    + ( SELECT COUNT(*)
        FROM [Step] AS [s]
        INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [s].[PersonAliasId]
        WHERE [s].[ForeignKey] = @ForeignKey AND [s].[CompletedDateTime] > [s].[StartDateTime]
            AND NOT EXISTS ( SELECT 1 FROM [StepHistory] AS [h]
                WHERE [h].[RelatedEntityId] = [s].[Id] AND [h].[EntityId] = [pa].[PersonId] AND [h].[Verb] = 'MODIFY'
                    AND [h].[ValueName] = 'Step Status' AND [h].[CreatedDateTime] = [s].[CompletedDateTime] ) )
    + ( SELECT COUNT(*)
        FROM [StepHistory] AS [h]
        WHERE NOT EXISTS ( SELECT 1 FROM [Step] AS [s]
            INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [s].[PersonAliasId]
            WHERE [s].[Id] = [h].[RelatedEntityId] AND [pa].[PersonId] = [h].[EntityId] ) );

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
-- why: Rock's Update Step Program Completions job records a completion for each person whose Complete steps cover every active step type in a program, dated from those steps, and points every step of theirs in the program at it. It runs for every program the dataset has steps in. Rock's job must find nothing to add or fix after install.
WITH [Programs] AS (
    SELECT DISTINCT [st].[StepProgramId] AS [Id]
    FROM [Step] AS [s]
    INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
    WHERE [s].[ForeignKey] = @ForeignKey
),
[PeopleWithAllSteps] AS (
    SELECT [st].[StepProgramId], [s].[PersonAliasId],
        MIN( COALESCE( [s].[StartDateTime], [s].[CreatedDateTime] ) ) AS [StartDateTime],
        MAX( COALESCE( [s].[CompletedDateTime], [s].[EndDateTime] ) ) AS [EndDateTime],
        MAX( [s].[CampusId] ) AS [CampusId]
    FROM [Step] AS [s]
    INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
    INNER JOIN [StepStatus] AS [ss] ON [ss].[Id] = [s].[StepStatusId] AND [ss].[IsCompleteStatus] = 1
    WHERE [st].[StepProgramId] IN ( SELECT [Id] FROM [Programs] ) AND [s].[ForeignKey] = @ForeignKey
    GROUP BY [st].[StepProgramId], [s].[PersonAliasId]
    HAVING COUNT( DISTINCT [s].[StepTypeId] ) = ( SELECT COUNT(*) FROM [StepType] AS [t] WHERE [t].[StepProgramId] = [st].[StepProgramId] AND [t].[IsActive] = 1 )
),
[Completions] AS (
    SELECT [c].[Id], [c].[StepProgramId], [c].[PersonAliasId], [c].[StartDateTime], [c].[EndDateTime], [c].[CampusId], [c].[StartDateKey]
    FROM [StepProgramCompletion] AS [c]
    WHERE [c].[StepProgramId] IN ( SELECT [Id] FROM [Programs] ) AND [c].[ForeignKey] = @ForeignKey
)
SELECT ( SELECT COUNT(*) FROM [PeopleWithAllSteps] AS [w] WHERE NOT EXISTS (
            SELECT 1 FROM [Completions] AS [c]
            WHERE [c].[StepProgramId] = [w].[StepProgramId] AND [c].[PersonAliasId] = [w].[PersonAliasId] AND [c].[StartDateTime] = [w].[StartDateTime]
                AND ISNULL( [c].[EndDateTime], '9999-01-01' ) = ISNULL( [w].[EndDateTime], '9999-01-01' )
                AND ISNULL( [c].[CampusId], 0 ) = ISNULL( [w].[CampusId], 0 ) AND [c].[StartDateKey] = CONVERT( INT, FORMAT( [w].[StartDateTime], 'yyyyMMdd' ) ) ) )
    + ( SELECT COUNT(*) FROM [Completions] AS [c] WHERE NOT EXISTS ( SELECT 1 FROM [PeopleWithAllSteps] AS [w] WHERE [w].[StepProgramId] = [c].[StepProgramId] AND [w].[PersonAliasId] = [c].[PersonAliasId] ) )
    + ( SELECT COUNT(*) FROM ( SELECT [StepProgramId], [PersonAliasId] FROM [Completions] GROUP BY [StepProgramId], [PersonAliasId] HAVING COUNT(*) > 1 ) AS [x] )
    + ( SELECT COUNT(*)
        FROM [Step] AS [s]
        INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
        LEFT JOIN [Completions] AS [c] ON [c].[StepProgramId] = [st].[StepProgramId] AND [c].[PersonAliasId] = [s].[PersonAliasId]
        WHERE [st].[StepProgramId] IN ( SELECT [Id] FROM [Programs] ) AND [s].[ForeignKey] = @ForeignKey
            AND ISNULL( [s].[StepProgramCompletionId], 0 ) <> ISNULL( [c].[Id], 0 ) );

-- check: people-match-the-dataset-card
-- why: Rock's installer shows the people count from datasets/{key}.json (@CardPeople). Returns how many people the load is off by when that is more than 2%.
SELECT CASE WHEN @CardPeople IS NOT NULL AND ABS( [c].[People] - @CardPeople ) > @CardPeople * 0.02 THEN ABS( [c].[People] - @CardPeople ) ELSE 0 END
FROM ( SELECT COUNT(*) AS [People] FROM [Person] WHERE [ForeignKey] = @ForeignKey ) AS [c];

-- check: households-match-the-dataset-card
-- why: Rock's installer shows the household count from datasets/{key}.json (@CardHouseholds). Returns how many households the load is off by when that is more than 2%.
SELECT CASE WHEN @CardHouseholds IS NOT NULL AND ABS( [c].[Households] - @CardHouseholds ) > @CardHouseholds * 0.02 THEN ABS( [c].[Households] - @CardHouseholds ) ELSE 0 END
FROM (
    SELECT COUNT(*) AS [Households]
    FROM [Group] AS [g]
    INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
    WHERE [g].[ForeignKey] = @ForeignKey AND [gt].[Guid] = '790E3215-3B10-442B-AF69-616C0DCB998E' ) AS [c];

-- check: student-step-belongs-to-a-student
-- why: The Student Path follows student check-ins (decision 115). Every Student Path step belongs to someone who was in grades 7 to 12 on its start and had checked in to the student service by then. Mission Trip is for high school students, and Student Leader for juniors and seniors. A student's grade comes from their graduation year and moves up each June 1, as check-in's does.
WITH [StudentSteps] AS (
    SELECT [st].[Guid] AS [StepTypeGuid], [s].[StartDateTime], [pa].[PersonId], [p].[GraduationYear],
        12 - ( [p].[GraduationYear] - CASE WHEN [s].[StartDateTime] >= DATEFROMPARTS( YEAR( [s].[StartDateTime] ), 6, 1 ) THEN YEAR( [s].[StartDateTime] ) + 1 ELSE YEAR( [s].[StartDateTime] ) END ) AS [Grade]
    FROM [Step] AS [s]
    INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
    INNER JOIN [StepProgram] AS [sp] ON [sp].[Id] = [st].[StepProgramId]
    INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [s].[PersonAliasId]
    INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
    WHERE [sp].[Guid] = 'C8AD9B16-5132-4ACB-954B-1E61C734AA67' AND [s].[ForeignKey] = @ForeignKey
)
SELECT COUNT(*)
FROM [StudentSteps] AS [x]
WHERE [x].[GraduationYear] IS NULL
    OR [x].[Grade] > 12
    OR [x].[Grade] < CASE [x].[StepTypeGuid] WHEN '8464EF05-F8E2-4DB3-9AC4-BC1EEE18CBC9' THEN 9 WHEN '724C69AF-20BC-4299-8FD5-A84ED2B4D42D' THEN 11 ELSE 7 END
    OR NOT EXISTS (
        SELECT 1
        FROM [Attendance] AS [a]
        INNER JOIN [AttendanceOccurrence] AS [o] ON [o].[Id] = [a].[OccurrenceId]
        INNER JOIN [Group] AS [g] ON [g].[Id] = [o].[GroupId]
        INNER JOIN [PersonAlias] AS [apa] ON [apa].[Id] = [a].[PersonAliasId]
        WHERE [apa].[PersonId] = [x].[PersonId] AND [a].[DidAttend] = 1 AND [o].[OccurrenceDate] <= [x].[StartDateTime]
            AND [g].[Name] IN ( 'Grades 7-8', 'Grades 9-12' ) AND [g].[GroupTypeId] IN ( SELECT [Id] FROM [GroupType] WHERE [Guid] IN ( '7A17235B-69AD-439B-BAB0-1A0A472DB96F', '9A88743B-F336-4404-B877-2A623689195D' ) ) );

-- check: student-path-decisions-and-leaders
-- why: Follow Jesus is a decision made once. Student Leader is held once, and ends at graduation in May of the student's graduation year, or stays open while they are still in school.
SELECT ( SELECT COUNT(*)
        FROM ( SELECT [s].[PersonAliasId]
            FROM [Step] AS [s]
            INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
            WHERE [st].[Guid] IN ( 'FAC9E970-7434-4067-9E1C-180043CE6FCB', '724C69AF-20BC-4299-8FD5-A84ED2B4D42D' ) AND [s].[ForeignKey] = @ForeignKey
            GROUP BY [s].[PersonAliasId], [st].[Guid]
            HAVING COUNT(*) > 1 ) AS [x] )
    + ( SELECT COUNT(*)
        FROM [Step] AS [s]
        INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
        INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [s].[PersonAliasId]
        INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
        WHERE [st].[Guid] = '724C69AF-20BC-4299-8FD5-A84ED2B4D42D' AND [s].[ForeignKey] = @ForeignKey
            AND ( ( [s].[EndDateTime] IS NOT NULL AND ( YEAR( [s].[EndDateTime] ) <> [p].[GraduationYear] OR MONTH( [s].[EndDateTime] ) <> 5 ) )
                OR ( [s].[EndDateTime] IS NULL AND DATEFROMPARTS( [p].[GraduationYear], 6, 1 ) <= CAST( GETDATE() AS DATE ) ) ) );

-- check: student-baptism-follows-the-student-service
-- why: A student's baptism goes on the Discipleship Path's Baptism step (decision 87). A child is baptized here, on a Sunday, after they started coming to the student service. The Baptism Date and Baptized Here agree with the step (checked above).
SELECT COUNT(*)
FROM [Step] AS [s]
INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [s].[PersonAliasId]
WHERE [st].[Guid] = '801CC43C-0641-4271-939E-75E428F31D06' AND [s].[ForeignKey] = @ForeignKey
    AND EXISTS (
        SELECT 1
        FROM [GroupMember] AS [gm]
        INNER JOIN [GroupTypeRole] AS [r] ON [r].[Id] = [gm].[GroupRoleId]
        WHERE [gm].[PersonId] = [pa].[PersonId] AND [r].[Guid] = 'C8B1814F-6AA7-4055-B2D7-48FE20429CB9' )
    AND ( DATEDIFF( DAY, '1900-01-07', [s].[StartDateTime] ) % 7 <> 0
        OR NOT EXISTS (
            SELECT 1
            FROM [Attendance] AS [a]
            INNER JOIN [AttendanceOccurrence] AS [o] ON [o].[Id] = [a].[OccurrenceId]
            INNER JOIN [Group] AS [g] ON [g].[Id] = [o].[GroupId]
            INNER JOIN [PersonAlias] AS [apa] ON [apa].[Id] = [a].[PersonAliasId]
            WHERE [apa].[PersonId] = [pa].[PersonId] AND [a].[DidAttend] = 1 AND [o].[OccurrenceDate] <= [s].[StartDateTime]
                AND [g].[Name] IN ( 'Grades 7-8', 'Grades 9-12' ) AND [g].[GroupTypeId] IN ( SELECT [Id] FROM [GroupType] WHERE [Guid] IN ( '7A17235B-69AD-439B-BAB0-1A0A472DB96F', '9A88743B-F336-4404-B877-2A623689195D' ) ) )
        OR NOT EXISTS (
            SELECT 1
            FROM [AttributeValue] AS [av]
            INNER JOIN [Attribute] AS [at] ON [at].[Id] = [av].[AttributeId]
            WHERE [at].[Guid] = '343A557E-92CC-4A75-B251-0DA732F5EB4E' AND [av].[EntityId] = [s].[Id] AND [av].[Value] = 'True' ) );

-- check: young-kid-checks-in-with-the-family
-- why: Kids from the nursery through 6th grade come only with their family (decision 146). Each of their check-ins is made by an adult of one of their families who was in the worship service that Sunday. A child whose families have no living, active adult checks in on their own, as before.
SELECT COUNT(*)
FROM [Attendance] AS [a]
INNER JOIN [AttendanceOccurrence] AS [o] ON [o].[Id] = [a].[OccurrenceId]
INNER JOIN [Group] AS [g] ON [g].[Id] = [o].[GroupId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [a].[PersonAliasId]
WHERE [a].[ForeignKey] = @ForeignKey AND [a].[DidAttend] = 1 AND [a].[AttendanceCodeId] IS NOT NULL
    AND [g].[GroupTypeId] IN ( SELECT [Id] FROM [GroupType] WHERE [Guid] IN ( 'CADB2D12-7836-44BC-8EEA-3C6AB22FD5E8', 'E3C8F7D6-5CEB-43BB-802F-66C3E734049E' ) )
    AND EXISTS (
        SELECT 1
        FROM [GroupMember] AS [kid]
        INNER JOIN [GroupMember] AS [adult] ON [adult].[GroupId] = [kid].[GroupId]
        INNER JOIN [GroupTypeRole] AS [r] ON [r].[Id] = [adult].[GroupRoleId]
        INNER JOIN [Person] AS [ap] ON [ap].[Id] = [adult].[PersonId]
        WHERE [kid].[PersonId] = [pa].[PersonId] AND [kid].[GroupTypeId] = [adult].[GroupTypeId]
            AND [r].[Guid] = '2639F9A5-2AAE-4E48-A8C3-4FFE86681E42'
            AND [ap].[IsDeceased] = 0 AND [ap].[RecordStatusValueId] = ( SELECT [Id] FROM [DefinedValue] WHERE [Guid] = '618F906C-C33D-4FA3-8AEF-E58CB7B63F1E' ) )
    AND NOT EXISTS (
        SELECT 1
        FROM [PersonAlias] AS [byAlias]
        INNER JOIN [GroupMember] AS [byMember] ON [byMember].[PersonId] = [byAlias].[PersonId]
        INNER JOIN [GroupTypeRole] AS [byRole] ON [byRole].[Id] = [byMember].[GroupRoleId]
        INNER JOIN [GroupMember] AS [kid] ON [kid].[GroupId] = [byMember].[GroupId] AND [kid].[PersonId] = [pa].[PersonId]
        INNER JOIN [PersonAlias] AS [wpa] ON [wpa].[PersonId] = [byAlias].[PersonId]
        INNER JOIN [Attendance] AS [w] ON [w].[PersonAliasId] = [wpa].[Id] AND [w].[DidAttend] = 1
        INNER JOIN [AttendanceOccurrence] AS [wo] ON [wo].[Id] = [w].[OccurrenceId] AND [wo].[SundayDate] = [o].[SundayDate]
        INNER JOIN [Group] AS [wg] ON [wg].[Id] = [wo].[GroupId]
        INNER JOIN [GroupType] AS [wgt] ON [wgt].[Id] = [wg].[GroupTypeId]
        WHERE [byAlias].[Id] = [a].[CheckedInByPersonAliasId]
            AND [byRole].[Guid] = '2639F9A5-2AAE-4E48-A8C3-4FFE86681E42'
            AND [wg].[Name] = 'Weekend Service' AND [wgt].[Guid] = '235BAE2B-5760-4763-AADF-3938F34BA100' );

-- check: onsite-gift-on-a-family-sunday
-- why: Checks and cash go in the offering only on a Sunday the family came (decision 146). A student who comes on their own does not make it a family Sunday, so every gift in a Sunday Offering batch falls on a Sunday when an adult of the giver's family attended.
SELECT COUNT(*)
FROM [FinancialTransaction] AS [t]
INNER JOIN [FinancialBatch] AS [b] ON [b].[Id] = [t].[BatchId]
INNER JOIN [PersonAlias] AS [gpa] ON [gpa].[Id] = [t].[AuthorizedPersonAliasId]
WHERE [t].[ForeignKey] = @ForeignKey AND [b].[Name] LIKE 'Sunday Offering %'
    AND NOT EXISTS (
        SELECT 1
        FROM [GroupMember] AS [giver]
        INNER JOIN [GroupMember] AS [adult] ON [adult].[GroupId] = [giver].[GroupId]
        INNER JOIN [GroupTypeRole] AS [r] ON [r].[Id] = [adult].[GroupRoleId]
        INNER JOIN [PersonAlias] AS [apa] ON [apa].[PersonId] = [adult].[PersonId]
        INNER JOIN [Attendance] AS [a] ON [a].[PersonAliasId] = [apa].[Id] AND [a].[DidAttend] = 1
        INNER JOIN [AttendanceOccurrence] AS [o] ON [o].[Id] = [a].[OccurrenceId]
        WHERE [giver].[PersonId] = [gpa].[PersonId] AND [giver].[GroupTypeId] = [adult].[GroupTypeId]
            AND [giver].[GroupTypeId] = ( SELECT [Id] FROM [GroupType] WHERE [Guid] = '790E3215-3B10-442B-AF69-616C0DCB998E' )
            AND [r].[Guid] = '2639F9A5-2AAE-4E48-A8C3-4FFE86681E42'
            AND [o].[SundayDate] = CAST( [t].[TransactionDateTime] AS DATE ) );

-- check: student-check-in-fits-the-grade
-- why: Only students in grades 7 to 12 check in to the student service, in the room for their grade on that day. A recent graduate's student check-ins end when they graduate (decision 148). The grade comes from the graduation year and moves up each June 1, as check-in's does.
SELECT COUNT(*)
FROM (
    SELECT [g].[Name],
        12 - ( [p].[GraduationYear] - CASE WHEN [o].[OccurrenceDate] >= DATEFROMPARTS( YEAR( [o].[OccurrenceDate] ), 6, 1 ) THEN YEAR( [o].[OccurrenceDate] ) + 1 ELSE YEAR( [o].[OccurrenceDate] ) END ) AS [Grade]
    FROM [Attendance] AS [a]
    INNER JOIN [AttendanceOccurrence] AS [o] ON [o].[Id] = [a].[OccurrenceId]
    INNER JOIN [Group] AS [g] ON [g].[Id] = [o].[GroupId]
    INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [a].[PersonAliasId]
    INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
    WHERE [a].[ForeignKey] = @ForeignKey AND [a].[DidAttend] = 1
        AND [g].[Name] IN ( 'Grades 7-8', 'Grades 9-12' ) AND [g].[GroupTypeId] IN ( SELECT [Id] FROM [GroupType] WHERE [Guid] IN ( '7A17235B-69AD-439B-BAB0-1A0A472DB96F', '9A88743B-F336-4404-B877-2A623689195D' ) )
) AS [x]
WHERE [x].[Grade] IS NULL
    OR ( [x].[Name] = 'Grades 7-8' AND [x].[Grade] NOT BETWEEN 7 AND 8 )
    OR ( [x].[Name] = 'Grades 9-12' AND [x].[Grade] NOT BETWEEN 9 AND 12 );

-- check: campus-account-dates-match-fund
-- why: Rock's giving pages hide an account outside its dates. A campus account with other dates than its fund would take gifts the fund says it cannot.
SELECT COUNT(*)
FROM [FinancialAccount] AS [child]
INNER JOIN [FinancialAccount] AS [fund] ON [fund].[Id] = [child].[ParentAccountId]
WHERE [child].[ForeignKey] = @ForeignKey AND [child].[CampusId] IS NOT NULL
    AND ( ISNULL( [child].[StartDate], '1900-01-01' ) <> ISNULL( [fund].[StartDate], '1900-01-01' )
        OR ISNULL( [child].[EndDate], '1900-01-01' ) <> ISNULL( [fund].[EndDate], '1900-01-01' ) );

-- check: pledge-is-on-a-top-level-fund
-- why: Rock's Pledge Analytics and statements count gifts to the pledged account and its child accounts. A pledge on a campus account would miss gifts to the fund's other campus accounts.
SELECT COUNT(*)
FROM [FinancialPledge] AS [fp]
INNER JOIN [FinancialAccount] AS [fa] ON [fa].[Id] = [fp].[AccountId]
WHERE [fp].[ForeignKey] = @ForeignKey AND [fa].[ParentAccountId] IS NOT NULL;

-- check: pledge-dates-fit-the-fund
-- why: Rock saves the date keys from the dates, and a pledge runs inside its fund's dates. Pledge Analytics and statements filter on both.
SELECT COUNT(*)
FROM [FinancialPledge] AS [fp]
INNER JOIN [FinancialAccount] AS [fa] ON [fa].[Id] = [fp].[AccountId]
WHERE [fp].[ForeignKey] = @ForeignKey
    AND ( [fp].[StartDate] > [fp].[EndDate]
        OR [fp].[StartDateKey] <> CONVERT( INT, FORMAT( [fp].[StartDate], 'yyyyMMdd' ) )
        OR [fp].[EndDateKey] <> CONVERT( INT, FORMAT( [fp].[EndDate], 'yyyyMMdd' ) )
        OR [fp].[StartDate] < ISNULL( [fa].[StartDate], [fp].[StartDate] )
        OR [fp].[EndDate] > ISNULL( [fa].[EndDate], [fp].[EndDate] ) );

-- check: one-pledge-per-giving-unit-and-fund
-- why: Pledge Analytics adds up pledges by giving unit. Two pledges to one fund from one unit would double the amount it shows as pledged.
SELECT COUNT(*)
FROM (
    SELECT [p].[GivingId], [fp].[AccountId]
    FROM [FinancialPledge] AS [fp]
    INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [fp].[PersonAliasId]
    INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
    WHERE [fp].[ForeignKey] = @ForeignKey
    GROUP BY [p].[GivingId], [fp].[AccountId]
    HAVING COUNT(*) > 1
) AS [x];

-- check: gift-posts-to-campus-account
-- why: A fund with campus accounts takes gifts only through them, so campus giving reports add up. A gift to the fund itself would count toward no campus.
SELECT COUNT(*)
FROM [FinancialTransactionDetail] AS [ftd]
INNER JOIN [FinancialAccount] AS [fa] ON [fa].[Id] = [ftd].[AccountId]
WHERE [ftd].[ForeignKey] = @ForeignKey
    AND EXISTS ( SELECT 1 FROM [FinancialAccount] AS [child] WHERE [child].[ParentAccountId] = [fa].[Id] AND [child].[CampusId] IS NOT NULL );

-- check: communication-list-members-allow-mass-email
-- why: Decision 133. Nobody who chose "No mass emails" or "Do not email" is on a communication list, so a list send never reaches someone who asked not to get one.
SELECT COUNT(*)
FROM [GroupMember] AS [gm]
INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [gm].[PersonId]
WHERE [gt].[Guid] = 'D1D95777-FFA3-CBB3-4A6D-658706DAED33' AND [g].[ForeignKey] = @ForeignKey
    AND [gm].[GroupMemberStatus] = 1 AND [p].[EmailPreference] <> 0;

-- check: newsletter-members-belong-to-the-campus
-- why: A campus newsletter holds that campus's adults with an active email (decision 133), so a campus's news never goes to another campus. An email that bounced after the person joined carries Rock's bounce note.
SELECT COUNT(*)
FROM [GroupMember] AS [gm]
INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [gm].[PersonId]
WHERE [gt].[Guid] = 'D1D95777-FFA3-CBB3-4A6D-658706DAED33' AND [g].[ForeignKey] = @ForeignKey AND [g].[CampusId] IS NOT NULL
    AND ( [p].[PrimaryCampusId] <> [g].[CampusId] OR ISNULL( [p].[Email], '' ) = '' OR [p].[AgeClassification] <> 1
        OR ( [p].[IsEmailActive] = 0 AND ISNULL( [p].[EmailNote], '' ) NOT LIKE 'Email experienced a Hard Bounce%' ) );

-- check: volunteers-list-follows-the-teams
-- why: The Volunteers list is everyone active on a serving team (decision 92), so a message to volunteers reaches exactly the people who serve. A volunteer who left the list from one of its emails stays on it as Inactive.
WITH [Serving] AS (
    SELECT DISTINCT [gm].[PersonId]
    FROM [GroupMember] AS [gm]
    INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
    INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
    INNER JOIN [Person] AS [p] ON [p].[Id] = [gm].[PersonId]
    WHERE [gt].[Guid] = '2C42B2D4-1C5F-4AD5-A9AD-08631B872AC4' AND [p].[ForeignKey] = @ForeignKey AND [g].[IsActive] = 1 AND [g].[IsArchived] = 0
        AND [gm].[GroupMemberStatus] = 1 AND [gm].[IsArchived] = 0 AND [p].[EmailPreference] = 0
),
[Listed] AS (
    SELECT [gm].[PersonId]
    FROM [GroupMember] AS [gm]
    INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
    WHERE [g].[Guid] = '7C040D39-B8F0-481E-95B3-9526D566C47F' AND [g].[ForeignKey] = @ForeignKey
)
SELECT ( SELECT COUNT(*) FROM [Serving] AS [s] WHERE NOT EXISTS ( SELECT 1 FROM [Listed] AS [l] WHERE [l].[PersonId] = [s].[PersonId] ) )
    + ( SELECT COUNT(*) FROM [Listed] AS [l] WHERE NOT EXISTS ( SELECT 1 FROM [Serving] AS [s] WHERE [s].[PersonId] = [l].[PersonId] ) );

-- check: small-group-leaders-list-follows-the-groups
-- why: The Small Group Leaders list is everyone who leads an active small group (decision 92), so leader messages reach exactly the leaders. A leader who left the list from one of its emails stays on it as Inactive.
WITH [Leading] AS (
    SELECT DISTINCT [gm].[PersonId]
    FROM [GroupMember] AS [gm]
    INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
    INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
    INNER JOIN [GroupTypeRole] AS [r] ON [r].[Id] = [gm].[GroupRoleId]
    INNER JOIN [Person] AS [p] ON [p].[Id] = [gm].[PersonId]
    WHERE [gt].[Guid] = '50FCFB30-F51A-49DF-86F4-2B176EA1820B' AND [p].[ForeignKey] = @ForeignKey AND [g].[IsActive] = 1 AND [g].[IsArchived] = 0
        AND [gm].[GroupMemberStatus] = 1 AND [gm].[IsArchived] = 0 AND [r].[IsLeader] = 1 AND [p].[EmailPreference] = 0
),
[Listed] AS (
    SELECT [gm].[PersonId]
    FROM [GroupMember] AS [gm]
    INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
    WHERE [g].[Guid] = '3D755601-598D-4E8F-A13D-9DF0F0087CA9' AND [g].[ForeignKey] = @ForeignKey
)
SELECT ( SELECT COUNT(*) FROM [Leading] AS [x] WHERE NOT EXISTS ( SELECT 1 FROM [Listed] AS [l] WHERE [l].[PersonId] = [x].[PersonId] ) )
    + ( SELECT COUNT(*) FROM [Listed] AS [l] WHERE NOT EXISTS ( SELECT 1 FROM [Leading] AS [x] WHERE [x].[PersonId] = [l].[PersonId] ) );

-- check: system-phone-numbers-are-reserved-and-unused
-- why: Content standard 2 and decision 93. The church's texting numbers are active, can text, use the reserved range, and are never a loaded person's number, so SMS Conversations never seems to text a member from their own phone.
SELECT COUNT(*)
FROM [SystemPhoneNumber] AS [spn]
WHERE [spn].[ForeignKey] = @ForeignKey
    AND ( [spn].[IsActive] = 0 OR [spn].[IsSmsEnabled] = 0 OR [spn].[IsSmsForwardingEnabled] = 1
        OR LEN( [spn].[Number] ) <> 12 OR LEFT( [spn].[Number], 2 ) <> '+1' OR SUBSTRING( [spn].[Number], 3, 3 ) NOT IN ( '602', '480', '623' ) OR SUBSTRING( [spn].[Number], 6, 5 ) <> '55501'
        OR EXISTS ( SELECT 1 FROM [PhoneNumber] AS [pn] INNER JOIN [Person] AS [p] ON [p].[Id] = [pn].[PersonId] WHERE [p].[ForeignKey] = @ForeignKey AND [pn].[Number] = SUBSTRING( [spn].[Number], 3, 10 ) ) );

-- check: communication-is-sent-and-never-sends-again
-- why: Decision 95. Rock's send job picks up any communication that is not Approved with a send time, so every sample communication is Approved, has its send time, and has the date key Rock fills from it.
SELECT COUNT(*)
FROM [Communication] AS [c]
WHERE [c].[ForeignKey] = @ForeignKey
    AND ( [c].[Status] <> 3 OR [c].[SendDateTime] IS NULL OR [c].[SendDateKey] IS NULL
        OR [c].[SendDateKey] <> CONVERT( INT, CONVERT( CHAR(8), [c].[SendDateTime], 112 ) ) );

-- check: no-recipient-waits-to-send
-- why: Decision 95. A Pending or Sending recipient is one Rock's send job would still send to.
SELECT COUNT(*)
FROM [CommunicationRecipient] AS [cr]
WHERE [cr].[ForeignKey] = @ForeignKey AND [cr].[Status] IN ( 0, 5 );

-- check: email-recipient-states-agree
-- why: Communication Detail counts and charts each status from these columns. A delivered or opened email was sent and confirmed, an opened one has its first open after the send, and a failed one says why. Only a bounce fails after Rock sent it.
SELECT COUNT(*)
FROM [CommunicationRecipient] AS [cr]
INNER JOIN [Communication] AS [c] ON [c].[Id] = [cr].[CommunicationId]
WHERE [cr].[ForeignKey] = @ForeignKey AND [c].[CommunicationType] = 1
    AND ( ( [cr].[Status] IN ( 1, 4 ) AND ( [cr].[SendDateTime] IS NULL OR [cr].[DeliveredDateTime] IS NULL OR [cr].[DeliveredDateTime] < [cr].[SendDateTime] ) )
        OR ( [cr].[Status] = 4 AND ( [cr].[OpenedDateTime] IS NULL OR [cr].[OpenedDateTime] < [cr].[SendDateTime] OR [cr].[OpenedClient] IS NULL ) )
        OR ( [cr].[Status] <> 4 AND [cr].[OpenedDateTime] IS NOT NULL )
        OR ( [cr].[Status] = 2 AND ( [cr].[StatusNote] IS NULL OR ( [cr].[SendDateTime] IS NOT NULL AND [cr].[StatusNote] NOT LIKE '5__ 5.%' ) ) ) );

-- check: opens-and-clicks-follow-an-open
-- why: Rock records an open or a click as an interaction on the communication's component, pointing at the recipient. Only an opened recipient has them, the first is an open at the recipient's open time, and nothing comes before the send.
SELECT COUNT(*)
FROM [Interaction] AS [i]
INNER JOIN [InteractionComponent] AS [ic] ON [ic].[Id] = [i].[InteractionComponentId]
INNER JOIN [InteractionChannel] AS [ch] ON [ch].[Id] = [ic].[InteractionChannelId]
LEFT JOIN [CommunicationRecipient] AS [cr] ON [cr].[Id] = [i].[EntityId] AND [cr].[CommunicationId] = [ic].[EntityId]
WHERE [ch].[Guid] = 'C88A187F-0343-4E7C-AF3F-79A8989DFA65' AND [i].[ForeignKey] = @ForeignKey
    AND ( [cr].[Id] IS NULL OR [cr].[Status] <> 4 OR [i].[InteractionDateTime] < [cr].[OpenedDateTime] OR [i].[PersonAliasId] <> [cr].[PersonAliasId]
        OR ( [i].[Operation] = 'Click' AND ISNULL( [i].[InteractionData], '' ) NOT LIKE 'https://rocksolidchurchdemo.com/%' ) );

-- check: unsubscribe-matches-the-person
-- why: Decision 131. A list unsubscribe leaves the person Inactive in that list, a Bulk one leaves them on "No mass emails" or stricter, and Rock records a level with every unsubscribe time.
SELECT COUNT(*)
FROM [CommunicationRecipient] AS [cr]
INNER JOIN [Communication] AS [c] ON [c].[Id] = [cr].[CommunicationId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [cr].[PersonAliasId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
WHERE [cr].[ForeignKey] = @ForeignKey
    AND ( ( [cr].[UnsubscribeDateTime] IS NULL AND [cr].[UnsubscribeLevel] IS NOT NULL ) OR ( [cr].[UnsubscribeDateTime] IS NOT NULL AND [cr].[UnsubscribeLevel] IS NULL )
        OR ( [cr].[UnsubscribeLevel] = 3 AND NOT EXISTS ( SELECT 1 FROM [GroupMember] AS [gm] WHERE [gm].[GroupId] = [c].[ListGroupId] AND [gm].[PersonId] = [p].[Id] AND [gm].[GroupMemberStatus] = 0 ) )
        OR ( [cr].[UnsubscribeLevel] = 2 AND [p].[EmailPreference] = 0 ) );

-- check: sent-email-has-its-history
-- why: Rock's email transport writes a "Sent" line on the person's History tab for every email it sends, so every sent email recipient has one.
SELECT ABS( ( SELECT COUNT(*) FROM [CommunicationRecipient] AS [cr] INNER JOIN [Communication] AS [c] ON [c].[Id] = [cr].[CommunicationId]
        WHERE [cr].[ForeignKey] = @ForeignKey AND [c].[CommunicationType] = 1 AND [cr].[SendDateTime] IS NOT NULL )
    - ( SELECT COUNT(*) FROM [History] AS [h] WHERE [h].[ForeignKey] = @ForeignKey AND [h].[Verb] = 'SENT' AND [h].[ValueName] = 'Communication' ) );

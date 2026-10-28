// <copyright>
// Copyright by the Spark Development Network
//
// Licensed under the Rock Community License (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.rockrms.com/license
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// </copyright>
//

using System.Data;
using System.Text.RegularExpressions;

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /// <summary>
    /// Writes the planned Rock Database logins with the password hash the person installing chose, the person
    /// history Rock's login save hook writes, and every loaded person's account protection profile
    /// (decisions D-logins-1 to D-logins-4).
    /// </summary>
    internal sealed class LoginsLoader
    {
        private static readonly Guid DatabaseAuthenticationGuid = new Guid( "4E9B798F-BB68-4C0E-9707-0928D15AB020" );
        private static readonly Guid PersonActivityHistoryCategoryGuid = new Guid( "0836845E-5ED8-4ABE-8787-3B61EF2F0FA5" );
        private static readonly Guid SecurityRoleGroupTypeGuid = new Guid( "AECE949F-704C-483E-A4FB-93D5E4720C4C" );
        private static readonly Regex BcryptHash = new Regex( @"^\$2[aby]\$\d\d\$[./A-Za-z0-9]{53}$" );

        private readonly RockDatabase _db;
        private readonly string _foreignKey;

        public LoginsLoader( RockDatabase db, string foreignKey )
        {
            _db = db;
            _foreignKey = foreignKey;
        }

        /// <summary>
        /// True when the text is a BCrypt hash, the form Rock's Database login stores.
        /// </summary>
        public static bool IsBcryptHash( string hash ) => hash != null && BcryptHash.IsMatch( hash );

        /// <summary>
        /// The usernames already in the database, such as Rock's own admin.
        /// </summary>
        public List<string> TakenUserNames()
        {
            return _db.Query( "SELECT [UserName] FROM [UserLogin]" ).Select( r => ( string ) r["UserName"] ).ToList();
        }

        /// <summary>
        /// The people who already have a login, such as Rock's admin person.
        /// </summary>
        public HashSet<int> PeopleWithLogins()
        {
            return _db.Query( "SELECT DISTINCT [PersonId] FROM [UserLogin] WHERE [PersonId] IS NOT NULL" ).Select( r => ( int ) r["PersonId"] ).ToHashSet();
        }

        /// <summary>
        /// When each person with the church app first opened it, from the devices this load wrote.
        /// </summary>
        public Dictionary<int, DateTime> AppFirstSeen()
        {
            return _db.Query( @"
SELECT [pa].[PersonId], MIN( [pd].[CreatedDateTime] ) AS [FirstSeen]
FROM [PersonalDevice] AS [pd]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [pd].[PersonAliasId]
WHERE [pd].[ForeignKey] = @ForeignKey
GROUP BY [pa].[PersonId]", P( "@ForeignKey", _foreignKey ) )
                .ToDictionary( r => ( int ) r["PersonId"], r => ( DateTime ) r["FirstSeen"] );
        }

        /// <summary>
        /// Writes the logins, each with the same password hash, and their person history.
        /// </summary>
        /// <exception cref="ArgumentException">The hash is not a BCrypt hash.</exception>
        public void Write( List<PlannedLogin> logins, string passwordHash )
        {
            if ( !IsBcryptHash( passwordHash ) )
            {
                throw new ArgumentException( "The login password hash is not a BCrypt hash, the form Rock's Database login stores." );
            }

            var entityTypeId = _db.RequireIdByGuid( "EntityType", DatabaseAuthenticationGuid );
            var providerName = ( string ) _db.Scalar( "SELECT ISNULL( [FriendlyName], [Name] ) FROM [EntityType] WHERE [Id] = @Id", P( "@Id", entityTypeId ) );
            var nextId = Convert.ToInt32( _db.Scalar( "SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [UserLogin]" ) );

            var rows = TableFactory.Create( "Id:int", "EntityTypeId:int", "UserName:string", "Password:string", "IsConfirmed:bool", "LastActivityDateTime:date", "LastLoginDateTime:date",
                "LastPasswordChangedDateTime:date", "IsLockedOut:bool", "IsPasswordChangeRequired:bool", "PersonId:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var history = new LoginHistory( _db, _foreignKey, providerName );
            foreach ( var login in logins )
            {
                var id = nextId++;
                rows.Rows.Add( id, entityTypeId, login.UserName, passwordHash, login.IsConfirmed, Value( login.LastLogin ), Value( login.LastLogin ),
                    login.Created, DBNull.Value, false, login.Person.PersonId, login.Guid, _foreignKey, login.Created, login.LastLogin ?? login.Created );
                history.Add( login, id );
            }

            _db.BulkInsert( "UserLogin", rows, keepIdentity: true );
            _db.BulkInsert( "History", history.Rows, keepIdentity: false );
        }

        /*
            10/27/26 - CLAUDE

            Rock sets a person's account protection profile in its Process Elevated Security job, not in Rock
            Cleanup, so this is the same rule in SQL (PersonService.UpdateAccountProtectionProfile): Extreme for a
            member of an active security role marked Extreme, High for one marked High or for anyone with an active
            scheduled gift or a saved payment account, Medium for anyone with a login, and Low otherwise. Archived
            memberships do not count. Only people this load wrote are set, after every step that writes logins,
            roles, or scheduled gifts.

            Reason: Before logins, every loaded person stayed Low, even givers with scheduled gifts.
        */

        /// <summary>
        /// Sets every loaded person's account protection profile the way Rock's Process Elevated Security job does.
        /// Returns how many changed.
        /// </summary>
        public int UpdateAccountProtection()
        {
            return _db.Execute( @"
UPDATE [p] SET [AccountProtectionProfile] = [x].[Profile]
FROM [Person] AS [p]
CROSS APPLY (
    SELECT CASE
        WHEN EXISTS ( SELECT 1 FROM [GroupMember] AS [gm]
            INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
            INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
            WHERE [gm].[PersonId] = [p].[Id] AND [gm].[IsArchived] = 0 AND [g].[IsActive] = 1
                AND ( [g].[IsSecurityRole] = 1 OR [gt].[Guid] = @SecurityRoleType ) AND [g].[ElevatedSecurityLevel] = 2 ) THEN 3
        WHEN EXISTS ( SELECT 1 FROM [GroupMember] AS [gm]
            INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
            INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
            WHERE [gm].[PersonId] = [p].[Id] AND [gm].[IsArchived] = 0 AND [g].[IsActive] = 1
                AND ( [g].[IsSecurityRole] = 1 OR [gt].[Guid] = @SecurityRoleType ) AND [g].[ElevatedSecurityLevel] = 1 )
            OR EXISTS ( SELECT 1 FROM [FinancialScheduledTransaction] AS [s]
                INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [s].[AuthorizedPersonAliasId]
                WHERE [pa].[PersonId] = [p].[Id] AND [s].[IsActive] = 1 )
            OR EXISTS ( SELECT 1 FROM [FinancialPersonSavedAccount] AS [sa]
                INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [sa].[PersonAliasId]
                WHERE [pa].[PersonId] = [p].[Id] ) THEN 2
        WHEN EXISTS ( SELECT 1 FROM [UserLogin] AS [u] WHERE [u].[PersonId] = [p].[Id] ) THEN 1
        ELSE 0
    END AS [Profile]
) AS [x]
WHERE [p].[ForeignKey] = @ForeignKey AND [p].[AccountProtectionProfile] <> [x].[Profile]",
                P( "@SecurityRoleType", SecurityRoleGroupTypeGuid ), P( "@ForeignKey", _foreignKey ) );
        }

        private static object Value( DateTime? value ) => value.HasValue ? value.Value : DBNull.Value;

        /*
            10/27/26 - CLAUDE

            Rock's login save hook (Rock/Model/CRM/UserLogin/UserLogin.SaveHook.cs) writes person history in the
            Activity category when a login is added: the authentication provider, the username, and Is Confirmed
            and Is Password Change Required, as Rock's bool text. Is Locked Out is left empty, as Rock's own
            account creation leaves it, so it writes no row. Each row names the login as related data, with the
            username as its caption.

            Reason: The person's History tab shows the login the way a church that made it in Rock sees it.
        */

        /// <summary>
        /// The person history rows Rock's login save hook writes for a new login.
        /// </summary>
        private sealed class LoginHistory
        {
            private readonly string _foreignKey;
            private readonly string _providerName;
            private readonly int _categoryId;
            private readonly int _personEntityTypeId;
            private readonly int _loginEntityTypeId;

            public DataTable Rows { get; } = TableFactory.Create( "IsSystem:bool", "CategoryId:int", "EntityTypeId:int", "EntityId:int", "Caption:string", "RelatedEntityTypeId:int", "RelatedEntityId:int",
                "Verb:string", "ChangeType:string", "ValueName:string", "OldValue:string", "NewValue:string", "IsSensitive:bool",
                "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            public LoginHistory( RockDatabase db, string foreignKey, string providerName )
            {
                _foreignKey = foreignKey;
                _providerName = providerName;
                _categoryId = db.RequireIdByGuid( "Category", PersonActivityHistoryCategoryGuid );
                _personEntityTypeId = Convert.ToInt32( db.Scalar( "SELECT [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.Person'" ) );
                _loginEntityTypeId = Convert.ToInt32( db.Scalar( "SELECT [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.UserLogin'" ) );
            }

            public void Add( PlannedLogin login, int loginId )
            {
                Row( login, loginId, "ADD", "Record", "Authentication Provider", _providerName );
                Row( login, loginId, "MODIFY", "Property", "User Login", login.UserName );
                Row( login, loginId, "MODIFY", "Property", "Is Confirmed", login.IsConfirmed.ToString() );
                Row( login, loginId, "MODIFY", "Property", "Is Password Change Required", false.ToString() );
            }

            private void Row( PlannedLogin login, int loginId, string verb, string changeType, string valueName, string newValue )
            {
                Rows.Rows.Add( false, _categoryId, _personEntityTypeId, login.Person.PersonId, login.UserName, _loginEntityTypeId, loginId,
                    verb, changeType, valueName, DBNull.Value, newValue, false, Guid.NewGuid(), _foreignKey, login.Created, login.Created );
            }
        }
    }
}

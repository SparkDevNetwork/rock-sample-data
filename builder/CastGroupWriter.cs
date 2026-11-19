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

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using RockSampleData.Loader;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

namespace RockSampleData.Builder
{
    /// <summary>
    /// The --plan-cast-groups mode: plans the Cast's small groups and writes them to cast/groups,
    /// then marks each Cast leader Leading in the household files. Running it again changes nothing.
    /// </summary>
    internal static class CastGroupWriter
    {
        /// <summary>
        /// Plans and writes the Cast's small groups. Returns the plans written.
        /// </summary>
        public static List<SmallGroupPlan> Run( string root, string seed )
        {
            var source = SampleDataSource.FromFolder( root );
            var rules = source.SmallGroups;
            var households = source.ReadHouseholds();
            var groupsFolder = Path.Combine( root, "cast", "groups" );

            // A pinned group keeps the Guid its file has. A new one gets the Guid the planner would give a group with its key.
            Guid GroupGuid( string key ) => File.Exists( Path.Combine( groupsFolder, $"{key}.json" ) )
                ? ReadGroupFile( Path.Combine( groupsFolder, $"{key}.json" ) )["guid"].GetValue<Guid>()
                : new SeededRandom( $"{seed}:small-groups:guid:main:{key}" ).NextGuid();
            var legacy = rules.Cast.Pins.Select( pin => new LegacyGroup(
                pin.Key,
                GroupGuid( pin.Key ),
                pin.Value.Leader == null ? null : PersonGuid( households, pin.Value.Leader ),
                pin.Value.HostHousehold )
            {
                Topic = pin.Value.Topic,
                Name = pin.Value.Name,
                Description = pin.Value.Description,
                Online = pin.Value.Online,
                MemberHouseholds = pin.Value.MemberHouseholds,
                PendingHouseholds = pin.Value.PendingHouseholds,
                LaunchesAgo = pin.Value.LaunchesAgo,
                ReadyToLead = pin.Value.ReadyToLead,
                Ended = pin.Value.Ended == null ? null : ( SmallGroupTestCase ) Enum.Parse( typeof( SmallGroupTestCase ), pin.Value.Ended )
            } ).ToList();

            // A Cast leader from Ready to Lead must already be on a serving team, not as its leader, since the rosters are hand-picked.
            var teamMembers = source.ReadCastServingTeams().Values.SelectMany( t => t.Members ).ToList();
            var volunteers = teamMembers.Where( m => m.Role != "Leader" ).Select( m => m.Guid ).Except( teamMembers.Where( m => m.Role == "Leader" ).Select( m => m.Guid ) ).ToHashSet();

            var planner = new SmallGroupPlanner( rules, PlanningClock.ForCast(), new GroupLaunchCalendar( source.Communications?.Events ), seed );
            // A campus pastor's family on another campus loads only with that campus (decision 199), so it joins no Cast group.
            var plans = planner.PlanCampus( new CampusPlanRequest
            {
                Campus = "main",
                Households = households.Where( h => h.CampusSlot == "main" ).ToList(),
                JoinPercent = ( _, people ) => people.Max( p => rules.Participation.Cast.GetValueOrDefault( p.ConnectionStatus ) ),
                ReadyToLeadCandidate = p => volunteers.Contains( p.Guid ),
                TestCases = rules.TestCases.MainCampus,
                Legacy = legacy
            } );

            foreach ( var plan in plans )
            {
                WriteText( Path.Combine( groupsFolder, $"{plan.Key}.json" ), ToFileText( plan ) );
            }

            // A group planned by an earlier run that this run no longer plans is removed.
            var keys = plans.Select( p => p.Key ).ToHashSet();
            foreach ( var file in Directory.GetFiles( groupsFolder, "*.json" ) )
            {
                var group = ReadGroupFile( file );
                var key = group["key"].GetValue<string>();
                if ( group["type"]?.GetValue<string>() == "small-group" && !keys.Contains( key ) && !rules.Cast.NotInPattern.Contains( key ) )
                {
                    File.Delete( file );
                }
            }

            var leaders = plans.Where( p => p.Lifecycle is ActiveLifecycle )
                .SelectMany( p => p.Members.Where( m => m.Role == SmallGroupRole.Leader ) )
                .Select( m => m.Guid )
                .ToHashSet();
            foreach ( var household in households.Where( h => h.Members.Any( p => leaders.Contains( p.Guid ) && p.ConnectionStatus != rules.Leaders.BecomeStatus ) ) )
            {
                var path = Path.Combine( root, "cast", "households", $"{household.Key}.json" );
                var text = File.ReadAllText( path );
                foreach ( var person in household.Members.Where( p => leaders.Contains( p.Guid ) ) )
                {
                    text = SetConnectionStatus( text, person.Guid, rules.Leaders.BecomeStatus );
                }

                WriteText( path, text );
            }

            return plans;
        }

        /// <summary>
        /// The Cast group file for a plan: the file header, then the plan.
        /// </summary>
        private static string ToFileText( SmallGroupPlan plan )
        {
            var body = JsonSerializer.SerializeToNode( plan, SmallGroupPlanJson.Options ).AsObject();
            var file = new JsonObject
            {
                ["$schema"] = "../group.schema.json",
                ["schemaVersion"] = 1
            };

            foreach ( var name in new[] { "key", "guid", "name" } )
            {
                file[name] = body[name].DeepClone();
            }

            file["type"] = "small-group";
            foreach ( var property in body.Where( p => !file.ContainsKey( p.Key ) ).ToList() )
            {
                file[property.Key] = property.Value?.DeepClone();
            }

            return file.ToJsonString( SmallGroupPlanJson.Options ) + Environment.NewLine;
        }

        /// <summary>
        /// Changes one person's connection status in a household file's text, leaving the rest of
        /// the file exactly as it was. The status follows the person's Guid inside their object.
        /// </summary>
        private static string SetConnectionStatus( string text, Guid person, string status )
        {
            var at = text.IndexOf( $"\"guid\": \"{person.ToString().ToUpperInvariant()}\"", StringComparison.Ordinal );
            var match = at < 0 ? Match.Empty : new Regex( "\"connectionStatus\": \"[A-Za-z]+\"" ).Match( text, at );
            var nextPerson = at < 0 ? -1 : text.IndexOf( "\"guid\": ", at + 1, StringComparison.Ordinal );
            if ( !match.Success || ( nextPerson >= 0 && match.Index > nextPerson ) )
            {
                throw new InvalidOperationException( $"Could not find the connection status for {person}." );
            }

            return text.Substring( 0, match.Index ) + $"\"connectionStatus\": \"{status}\"" + text.Substring( match.Index + match.Length );
        }

        private static Guid PersonGuid( List<CastHousehold> households, string reference )
        {
            var parts = reference.Split( '/' );
            return households.Single( h => h.Key == parts[0] ).Members.Single( p => p.Key == parts[1] ).Guid;
        }

        private static JsonObject ReadGroupFile( string path )
        {
            return JsonNode.Parse( File.ReadAllText( path ) ).AsObject();
        }

        /// <summary>
        /// Writes a file only when its text changed, so an unchanged run touches nothing.
        /// </summary>
        private static void WriteText( string path, string text )
        {
            if ( !File.Exists( path ) || File.ReadAllText( path ) != text )
            {
                File.WriteAllText( path, text );
            }
        }
    }
}

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

using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    internal sealed partial class DataIntegrityLoader
    {
        private static readonly Guid TextFieldTypeGuid = new Guid( "9C204CD0-1233-41C5-818A-C5DA439445AA" );

        /// <summary>
        /// The six Data Automation rules' system setting keys, as Rock's SystemSetting constants name them. A rule the file
        /// gives under another key is refused, since Rock would never read it.
        /// </summary>
        internal static readonly string[] RuleKeys =
        {
            "core_DataAutomationReactivatePeople",
            "core_DataAutomationInactivatePeople",
            "core_DataAutomationUpdateFamilyCampus",
            "core_DataAutomationAdultChildren",
            "core_DataAutomationUpdatePersonConnectionStatus",
            "core_DataAutomationUpdateFamilyStatus"
        };

        /// <summary>
        /// The settings properties where Rock stores a database Id, and the table each Id is in. The file gives Guids.
        /// </summary>
        private static readonly Dictionary<string, string> IdProperties = new Dictionary<string, string>
        {
            ["AttendanceInGroupType"] = "GroupType",
            ["PersonAttributes"] = "Attribute",
            ["IncludeDataView"] = "DataView",
            ["ExcludeDataView"] = "DataView",
            ["NotInDataview"] = "DataView",
            ["ParentRelationshipId"] = "GroupTypeRole",
            ["SiblingRelationshipId"] = "GroupTypeRole",
            ["WorkflowTypeIds"] = "WorkflowType",
            ["ExcludeSchedules"] = "Schedule"
        };

        /// <summary>
        /// The settings maps keyed by a defined value Id, whose values are data view Ids.
        /// </summary>
        private static readonly HashSet<string> StatusMappings = new HashSet<string> { "ConnectionStatusValueIdDataviewIdMapping", "GroupStatusValueIdDataviewIdMapping" };

        /*
            11/23/26 - CLAUDE

            Rock's Data Integrity Settings page saves each Data Automation rule through SystemSettings.SetValue: the rule's
            settings class as JSON, in the default value of a text attribute qualified as a system setting. A new Rock has
            none of the six, so the page shows each class's defaults, all off. Each rule here is configured the way a church
            sets it up and saved off, so Rock's Data Automation job, which runs every Tuesday at 4 AM, changes nobody. The
            General panel's gender auto-fill confidence keeps Rock's 99.9. Verify reads each rule back with Rock's own
            classes and runs Rock's job.

            Reason: Jon chose configured but off (issue 2 question 1, decision D-integrity-7).
        */

        /// <summary>
        /// Saves each Data Automation rule in its system setting, with this database's Ids.
        /// </summary>
        private string WriteSettings( DataIntegritySettings settings )
        {
            var fieldTypeId = _db.RequireIdByGuid( "FieldType", TextFieldTypeGuid );
            var saved = new List<string>();
            foreach ( var rule in settings.Rules )
            {
                if ( !RuleKeys.Contains( rule.Key ) )
                {
                    throw new InvalidDataException( $"configuration/data-integrity.json has a rule under {rule.Key}, which is not one of Rock's Data Automation settings." );
                }

                var node = JsonNode.Parse( rule.Value.GetRawText() ).AsObject();
                foreach ( var property in node.ToList() )
                {
                    if ( IdProperties.TryGetValue( property.Key, out var table ) )
                    {
                        node[property.Key] = property.Value is JsonArray list
                            ? new JsonArray( list.Select( item => ( JsonNode ) JsonValue.Create( RequireId( table, item, rule.Key ) ) ).ToArray() )
                            : property.Value == null ? null : JsonValue.Create( RequireId( table, property.Value, rule.Key ) );
                    }
                    else if ( StatusMappings.Contains( property.Key ) )
                    {
                        var mapping = new JsonObject();
                        foreach ( var pair in property.Value.AsObject() )
                        {
                            mapping[RequireId( "DefinedValue", JsonValue.Create( pair.Key ), rule.Key ).ToString()] = pair.Value == null ? null : JsonValue.Create( _db.IdByGuid( "DataView", Guid.Parse( pair.Value.GetValue<string>() ) ) );
                        }

                        node[property.Key] = mapping;
                    }
                }

                var json = node.ToJsonString();
                var name = Regex.Replace( Regex.Replace( rule.Key, @"(\P{Ll})(\P{Ll}\p{Ll})", "$1 $2" ), @"(\p{Ll})(\P{Ll})", "$1 $2" );
                var updated = _db.Execute( "UPDATE [Attribute] SET [DefaultValue] = @Value, [ModifiedDateTime] = @Now WHERE [EntityTypeId] IS NULL AND [EntityTypeQualifierColumn] = 'SystemSetting' AND [Key] = @Key",
                    P( "@Value", json ), P( "@Now", _dates.Anchor ), P( "@Key", rule.Key ) );
                if ( updated == 0 )
                {
                    _db.Execute( @"
INSERT INTO [Attribute] ( [IsSystem], [FieldTypeId], [EntityTypeQualifierColumn], [EntityTypeQualifierValue], [Key], [Name], [Order], [IsGridColumn], [DefaultValue], [IsMultiValue], [IsRequired], [Guid], [CreatedDateTime], [ModifiedDateTime], [ForeignKey] )
VALUES ( 0, @FieldType, 'SystemSetting', '', @Key, @Name, 0, 0, @Value, 0, 0, @Guid, @Now, @Now, @ForeignKey )",
                        P( "@FieldType", fieldTypeId ), P( "@Key", rule.Key ), P( "@Name", name ), P( "@Value", json ), P( "@Guid", SeededRandom.StableGuid( $"data-integrity:setting:{rule.Key}" ) ),
                        P( "@Now", _dates.Anchor ), P( "@ForeignKey", _foreignKey ) );
                }

                var isOn = node["IsEnabled"]?.GetValue<bool>() == true;
                saved.Add( $"{rule.Key.Replace( "core_DataAutomation", string.Empty )} {( isOn ? "on" : "off" )}" );
            }

            return $"  Data Automation rules: {string.Join( ", ", saved )}";
        }

        /// <summary>
        /// The Id of the row with the Guid a settings value gives.
        /// </summary>
        private int RequireId( string table, JsonNode value, string ruleKey )
        {
            var text = value?.GetValue<string>();
            if ( !Guid.TryParse( text, out var guid ) )
            {
                throw new InvalidDataException( $"configuration/data-integrity.json's {ruleKey} gives \"{text}\" where Rock stores a {table} Id. Give the {table}'s Guid." );
            }

            return _db.IdByGuid( table, guid ) ?? throw new InvalidDataException( $"configuration/data-integrity.json's {ruleKey} names {table} {guid}, which this Rock does not have." );
        }
    }
}

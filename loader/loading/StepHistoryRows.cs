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
using System.Globalization;

using RockSampleData.Loader.Database;

namespace RockSampleData.Loader.Loading
{
    /*
        10/23/26 - CLAUDE

        Rock's step save hook (Rock/Model/Engagement/Step/Step.SaveHook.cs) writes person history: one
        "Added" row when a step is made, with the campus as related data, and "Modify" rows when its status,
        end, or completion date changes later. A step completed after its start (a class, a Pending volunteer
        who became Active, or an eRA step its family exits) was added In Progress and completed on that day.
        A step with an end date was ended on that day. Dates use Rock's US short date format, as the hook writes them.

        Reason: The person's History tab shows each step the way a church that recorded it in Rock sees it.
    */

    /// <summary>
    /// The person history rows Rock's step save hook writes for each step.
    /// </summary>
    internal sealed class StepHistoryRows
    {
        private static readonly Guid PersonStepHistoryCategoryGuid = new Guid( "517BB2D3-0A50-4132-818E-63BB3C81EAE9" );

        private readonly string _foreignKey;
        private readonly int _categoryId;
        private readonly int _personEntityTypeId;
        private readonly int _stepEntityTypeId;
        private readonly Dictionary<int, string> _campusNames;

        public DataTable Rows { get; } = TableFactory.Create( "IsSystem:bool", "CategoryId:int", "EntityTypeId:int", "EntityId:int", "Caption:string", "RelatedEntityTypeId:int", "RelatedEntityId:int",
            "Verb:string", "ChangeType:string", "ValueName:string", "OldValue:string", "NewValue:string", "RelatedData:string", "IsSensitive:bool",
            "SourceOfChange:string", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

        /// <summary>
        /// What Rock records as the source of the change, such as the job that saved the step, or null for a person's own edit.
        /// </summary>
        public string SourceOfChange { get; init; }

        public StepHistoryRows( RockDatabase db, string foreignKey )
        {
            _foreignKey = foreignKey;
            _categoryId = db.RequireIdByGuid( "Category", PersonStepHistoryCategoryGuid );
            _personEntityTypeId = Convert.ToInt32( db.Scalar( "SELECT [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.Person'" ) );
            _stepEntityTypeId = Convert.ToInt32( db.Scalar( "SELECT [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.Step'" ) );
            _campusNames = db.Query( "SELECT [Id], [Name] FROM [Campus]" ).ToDictionary( r => Convert.ToInt32( r["Id"] ), r => ( string ) r["Name"] );
        }

        /// <summary>
        /// Adds the rows for one step: added on its start, then completed and ended on their days when they come later.
        /// </summary>
        public void Add( int personId, int stepId, string typeName, int? campusId, DateTime start, DateTime? completed, DateTime? end )
        {
            Row( personId, stepId, typeName, "STEPADDED", typeName, null, null, campusId.HasValue ? _campusNames.GetValueOrDefault( campusId.Value ) : null, start );

            if ( completed.HasValue && completed.Value > start )
            {
                Row( personId, stepId, typeName, "MODIFY", "Step Status", "In Progress", "Complete", null, completed.Value );
                Row( personId, stepId, typeName, "MODIFY", "Completed", string.Empty, ShortDate( completed.Value ), null, completed.Value );
            }

            if ( end.HasValue && end.Value > start )
            {
                Row( personId, stepId, typeName, "MODIFY", "Step End", string.Empty, ShortDate( end.Value ), null, end.Value );
            }
        }

        private void Row( int personId, int stepId, string caption, string verb, string valueName, string oldValue, string newValue, string relatedData, DateTime when )
        {
            Rows.Rows.Add( false, _categoryId, _personEntityTypeId, personId, caption, _stepEntityTypeId, stepId, verb, "Record", valueName,
                ( object ) oldValue ?? DBNull.Value, ( object ) newValue ?? DBNull.Value, ( object ) relatedData ?? DBNull.Value, false,
                ( object ) SourceOfChange ?? DBNull.Value, Guid.NewGuid(), _foreignKey, when, when );
        }

        private static string ShortDate( DateTime date ) => date.ToString( "M/d/yyyy", CultureInfo.InvariantCulture );
    }
}

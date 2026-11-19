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

using RockSampleData.Loader.Database;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        11/17/26 - CLAUDE

        A church that takes gifts in another currency turns on Enable Foreign Currency on Rock's Transaction Detail and
        Transaction List blocks. Off, which is Rock's default, those blocks hide the currency code and the foreign
        amounts, and staff cannot enter them. This is church setup, so it loads in every load, like the campaign's page
        settings (decision 126).

        Reason: Staff see the Canadian checks as Canadian (decision D-uncommon-3).
    */

    /// <summary>
    /// Turns on the foreign currency settings of Rock's finance blocks.
    /// </summary>
    internal static class ForeignCurrencySetup
    {
        private const string SettingKey = "EnableForeignCurrency";

        /// <summary>
        /// Sets Enable Foreign Currency on every block of the block types configuration/foreign-currency.json lists.
        /// Throws when Rock has no such block type.
        /// </summary>
        public static void Load( RockDatabase db, SampleDataSource source, string foreignKey )
        {
            var config = source.ForeignCurrency;
            if ( config == null )
            {
                return;
            }

            var count = 0;
            foreach ( var blockTypeGuid in config.BlockTypes )
            {
                var blockType = db.Query( "SELECT [Id], [Name] FROM [BlockType] WHERE [Guid] = @Guid", P( "@Guid", blockTypeGuid ) ).FirstOrDefault()
                    ?? throw new InvalidOperationException( $"configuration/foreign-currency.json names the block type {blockTypeGuid}, which this Rock does not have." );
                foreach ( var block in db.Query( "SELECT [Id] FROM [Block] WHERE [BlockTypeId] = @BlockTypeId", P( "@BlockTypeId", blockType["Id"] ) ) )
                {
                    BlockSettings.Set( db, block["Id"], blockType["Id"], ( string ) blockType["Name"], new Dictionary<string, string> { [SettingKey] = "True" }, foreignKey );
                    count++;
                }
            }

            db.Log.Write( $"  Foreign currency: {config.CurrencyCode} shown on {count} finance block{( count == 1 ? "" : "s" )}" );
        }
    }
}

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

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;

namespace RockSampleData.Loader.Loading
{
    /// <summary>
    /// What gifts and registration payments share: Rock's currency, card, source, and transaction type values, the
    /// payment detail rows a giver's wallet makes, and the codes the gateway gives. Looked-up Ids are kept.
    /// </summary>
    internal sealed class PaymentMethods
    {
        public static readonly Guid WebsiteSourceGuid = new Guid( "7D705CE7-7B11-4342-A58E-53617C5B4E69" );

        private static readonly Dictionary<string, Guid> CurrencyGuids = new Dictionary<string, Guid>
        {
            ["Cash"] = new Guid( "F3ADC889-1EE8-4EB6-B3FD-8C10F3C8AF93" ),
            ["Check"] = new Guid( "8B086A19-405A-451F-8D44-174E92D6B402" ),
            ["Credit Card"] = new Guid( "928A2E04-C77B-4282-888F-EC549CEE026A" ),
            ["ACH"] = new Guid( "DABEE8FD-AEDF-43E1-8547-4C97FA14D9B6" ),
            ["Apple Pay"] = new Guid( "D42C4DF7-1AE9-4DDE-ADA2-774B866B798C" ),
            ["Non-Cash Asset"] = new Guid( "7950FF66-80EE-E8AB-4A77-4A13EDEB7513" )
        };

        private readonly RockDatabase _db;
        private readonly Dictionary<string, int> _definedValueIds = new Dictionary<string, int>();

        public PaymentMethods( RockDatabase db )
        {
            _db = db;
        }

        /// <summary>
        /// A new payment detail table, with the columns <see cref="AddPaymentDetail"/> fills.
        /// </summary>
        public static DataTable PaymentDetailTable() => TableFactory.Create( "Id:int", "CurrencyTypeValueId:int", "CreditCardTypeValueId:int", "AccountNumberMasked:string", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

        /// <summary>
        /// A gateway transaction code: T and nine digits.
        /// </summary>
        public static string TransactionCode( SeededRandom random ) => $"T{random.Between( 100000000, 999999999 )}";

        /// <summary>
        /// The gateway's Id for a scheduled transaction.
        /// </summary>
        public static string GatewayScheduleId( int scheduledTransactionId ) => $"SCH{scheduledTransactionId:D7}";

        /// <summary>
        /// Adds a payment detail for one of the wallet's payment methods: its currency, its card brand for a card, and its masked number.
        /// </summary>
        public void AddPaymentDetail( DataTable rows, int id, string currency, Wallet wallet, Guid guid, string foreignKey, DateTime created, DateTime modified )
        {
            rows.Rows.Add( id, CurrencyId( currency ), CardType( currency, wallet ), wallet.Masked( currency ) ?? ( object ) DBNull.Value, guid, foreignKey, created, modified );
        }

        public int CurrencyId( string currency ) => DefinedValueId( CurrencyGuids[currency] );

        /// <summary>
        /// The card brand's Credit Card Type value for a card, or null for every other currency.
        /// </summary>
        public object CardType( string currency, Wallet wallet ) => currency == "Credit Card" ? DefinedValueIdByName( "Credit Card Type", wallet.CardBrand ) : DBNull.Value;

        public int DefinedValueId( Guid guid )
        {
            var key = guid.ToString();
            if ( !_definedValueIds.TryGetValue( key, out var id ) )
            {
                id = _db.RequireIdByGuid( "DefinedValue", guid );
                _definedValueIds[key] = id;
            }

            return id;
        }

        /// <summary>
        /// A defined value's Id by its type's name and its value, or null when Rock has none.
        /// </summary>
        public object DefinedValueIdByName( string definedTypeName, string value )
        {
            var key = $"{definedTypeName}:{value}";
            if ( !_definedValueIds.TryGetValue( key, out var id ) )
            {
                var found = _db.Scalar( "SELECT TOP 1 [dv].[Id] FROM [DefinedValue] AS [dv] INNER JOIN [DefinedType] AS [dt] ON [dt].[Id] = [dv].[DefinedTypeId] WHERE [dt].[Name] = @Type AND [dv].[Value] = @Value",
                    RockDatabase.P( "@Type", definedTypeName ), RockDatabase.P( "@Value", value ) );
                if ( found == null )
                {
                    return DBNull.Value;
                }

                id = Convert.ToInt32( found );
                _definedValueIds[key] = id;
            }

            return id;
        }
    }
}

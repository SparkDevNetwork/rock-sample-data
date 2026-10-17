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

namespace RockSampleData.Loader.Generation
{
    /*
        10/15/26 - CLAUDE

        Every giver gets one wallet, so the same card, bank account, and checkbook show up
        on all of their gifts, the way they would in a real church's records. Before this,
        each gift picked new random digits, so one person seemed to use a new card every week
        and their check numbers jumped around.

        Banks are fictional. Every routing number starts with 39, a prefix no real routing
        number uses, and also fails the routing checksum, so no sample check can point at a
        real bank.

        Reason: Payment details must agree across gifts and with the check images.
    */

    /// <summary>
    /// One giver's payment methods: a card, a bank account for ACH, and a checkbook.
    /// </summary>
    internal sealed class Wallet
    {
        /// <summary>
        /// Fictional banks for personal checks.
        /// </summary>
        public static readonly string[] Banks =
        {
            "Desert Sky Community Bank",
            "Ocotillo Federal Credit Union",
            "Copper Wash Savings Bank",
            "Agave Trust Bank",
            "Javelina Valley Credit Union"
        };

        private static readonly Dictionary<string, double> CardBrandWeights = new Dictionary<string, double> { ["Visa"] = 60, ["MasterCard"] = 30, ["Amex"] = 7, ["Discover"] = 3 };

        private readonly SeededRandom _random;

        public Wallet( string seed, Guid personGuid )
        {
            _random = new SeededRandom( $"{seed}:wallet:{personGuid}" );
            CardBrand = _random.Pick( CardBrandWeights );
            CardLast4 = _random.Between( 1000, 9999 ).ToString();
            ApplePayLast4 = _random.Between( 1000, 9999 ).ToString();
            Bank = _random.Pick( Banks );
            RoutingNumber = FictionalRoutingNumber( _random );
            AccountNumber = _random.Between( 10000, 99999 ).ToString() + _random.Between( 1000, 9999 ).ToString();
            HandwritingIndex = _random.Between( 0, 99 );
            NextCheckNumber = _random.Between( 1001, 4800 );
        }

        public string CardBrand { get; }

        public string CardLast4 { get; }

        public string ApplePayLast4 { get; }

        /// <summary>
        /// The bank behind both ACH gifts and checks.
        /// </summary>
        public string Bank { get; }

        public string RoutingNumber { get; }

        public string AccountNumber { get; }

        public string AccountLast4 => AccountNumber.Substring( AccountNumber.Length - 4 );

        /// <summary>
        /// Picks this writer's handwriting and signature style, so all their checks match.
        /// </summary>
        public int HandwritingIndex { get; }

        private int NextCheckNumber { get; set; }

        /// <summary>
        /// The masked number Rock stores for a payment method, or null when Rock stores none (cash).
        /// </summary>
        public string Masked( string currency )
        {
            return currency switch
            {
                "Credit Card" => $"************{CardLast4}",
                "Apple Pay" => $"************{ApplePayLast4}",
                "ACH" or "Check" => $"*****{AccountLast4}",
                _ => null
            };
        }

        /// <summary>
        /// Tears out the next check. Now and then a few numbers are skipped, for checks written elsewhere.
        /// </summary>
        public int TakeCheckNumber()
        {
            if ( _random.Chance( 0.25 ) )
            {
                NextCheckNumber += _random.Between( 1, 4 );
            }

            return NextCheckNumber++;
        }

        /// <summary>
        /// A nine-digit routing number that cannot belong to a real bank: it starts with 39
        /// (never assigned) and fails the ABA checksum.
        /// </summary>
        private static string FictionalRoutingNumber( SeededRandom random )
        {
            while ( true )
            {
                var digits = "39" + random.Between( 1000000, 9999999 );
                var d = digits.Select( c => c - '0' ).ToArray();
                var checksum = ( 3 * ( d[0] + d[3] + d[6] ) ) + ( 7 * ( d[1] + d[4] + d[7] ) ) + d[2] + d[5] + d[8];
                if ( checksum % 10 != 0 )
                {
                    return digits;
                }
            }
        }
    }
}

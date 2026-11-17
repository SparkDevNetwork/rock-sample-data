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

using RockSampleData.Loader.Model;

namespace RockSampleData.Loader.Generation
{
    /*
        11/16/26 - CLAUDE

        Every generated person and business gets a number no one else has (content standard 2). The pool holds
        555-0100 to 555-0199, which is fictional in every area code, in the Phoenix codes and then in the other US
        codes from configuration/phones.json, each block in a seeded order, without the Cast's numbers and the
        church's texting numbers. It has its own stream, so taking a number moves no other choice in a household.

        Reason: Three area codes drawn with replacement gave only 300 numbers, so dozens of people shared each one,
        and a texted STOP, a check-in phone search, or the duplicate finder reached strangers (decision 259).
    */

    /// <summary>
    /// The reserved phone numbers a load gives generated people and businesses, each used once.
    /// </summary>
    internal sealed class PhoneNumberPool
    {
        private readonly List<string> _numbers;
        private int _next;

        /// <param name="phones">The area codes.</param>
        /// <param name="seed">The load's seed.</param>
        /// <param name="taken">Numbers the pool never gives: the Cast's and the church's texting numbers.</param>
        public PhoneNumberPool( PhoneConfiguration phones, string seed, IEnumerable<string> taken )
        {
            var random = new SeededRandom( $"{seed}:phone-numbers" );
            var takenNumbers = taken.ToHashSet();
            var local = Shuffled( phones.LocalAreaCodes, random );
            var other = Shuffled( phones.OtherAreaCodes.Values.SelectMany( c => c ), random );
            _numbers = local.Concat( other ).Where( n => !takenNumbers.Contains( n ) ).ToList();
        }

        /// <summary>
        /// The next number no one has.
        /// </summary>
        public string Next()
        {
            if ( _next == _numbers.Count )
            {
                throw new InvalidOperationException( $"configuration/phones.json has {_numbers.Count:N0} free phone numbers, too few for this many generated people and businesses. Add area codes." );
            }

            return _numbers[_next++];
        }

        /// <summary>
        /// Every reserved number in the area codes, in a seeded order. The codes are sorted first, so the order
        /// does not depend on how the file lists them.
        /// </summary>
        private static List<string> Shuffled( IEnumerable<string> areaCodes, SeededRandom random )
        {
            return areaCodes.Distinct().OrderBy( c => c, StringComparer.Ordinal )
                .SelectMany( c => Enumerable.Range( 100, 100 ).Select( n => $"{c}5550{n}" ) )
                .OrderBy( _ => random.Between( 0.0, 1.0 ) )
                .ToList();
        }
    }
}

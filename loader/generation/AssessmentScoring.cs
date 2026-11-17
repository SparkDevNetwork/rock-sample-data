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

using System.Globalization;
using System.Text;

using RockSampleData.Loader.Model;

namespace RockSampleData.Loader.Generation
{
    /*
        11/16/26 - CLAUDE

        Rock scores each assessment in its service (Rock/Model/CRM/Assessment), saves the result as person attribute
        values, and its block saves the answers as the Assessment's AssessmentResultData. The loader makes each
        person's answers, scores them with this port, and writes both from the one score, so the answers and the
        attributes always agree. The arithmetic is Rock's: the same doubles, Math.Round to one place, the
        zScoreToPercentage lookup, decimals where Rock uses decimals, and ties broken in Rock's order. Values and the
        answer data are formatted the way Rock saves them. The verify tool scores every saved answer set with Rock's
        own services and compares.

        Reason: decision D-assessments-1.
    */

    /// <summary>
    /// One person's answers to one assessment.
    /// </summary>
    internal abstract class AssessmentAnswers
    {
    }

    /// <summary>
    /// DISC answers: for each of the 30 questions, in order, the letter of the response picked as Most and as Least.
    /// </summary>
    internal sealed class DiscAnswers : AssessmentAnswers
    {
        public DiscAnswers( IReadOnlyList<(char Most, char Least)> questions )
        {
            Questions = questions;
        }

        public IReadOnlyList<(char Most, char Least)> Questions { get; }
    }

    /// <summary>
    /// Answers on a scale: each question's code and score, in the order the person answered them.
    /// </summary>
    internal sealed class ScaleAnswers : AssessmentAnswers
    {
        public ScaleAnswers( IReadOnlyList<KeyValuePair<string, int>> answers )
        {
            Answers = answers;
        }

        public IReadOnlyList<KeyValuePair<string, int>> Answers { get; }
    }

    /// <summary>
    /// A scored assessment: the person attribute values Rock's service saves, in Rock's text, and the answer data.
    /// </summary>
    internal sealed class AssessmentScore
    {
        private readonly Func<double, string> _resultData;

        public AssessmentScore( IReadOnlyList<KeyValuePair<string, string>> values, IReadOnlyList<string> ranking, string summary, Func<double, string> resultData )
        {
            Values = values;
            Ranking = ranking;
            Summary = summary;
            _resultData = resultData;
        }

        /// <summary>
        /// Each person attribute's key and value, except the save date, which the writer adds.
        /// </summary>
        public IReadOnlyList<KeyValuePair<string, string>> Values { get; }

        /// <summary>
        /// The dimensions, strongest first.
        /// </summary>
        public IReadOnlyList<string> Ranking { get; }

        /// <summary>
        /// A few words for the load summary, such as a DISC personality type.
        /// </summary>
        public string Summary { get; }

        /// <summary>
        /// The AssessmentResultData Rock's block saves, for a time to take in seconds.
        /// </summary>
        public string ResultData( double secondsToTake ) => _resultData( secondsToTake );
    }

    /// <summary>
    /// What the scoring reads from Rock's defined values: names, profiles, and the motivators' attribute keys and themes.
    /// </summary>
    internal sealed class AssessmentReference
    {
        /// <summary>
        /// Each DISC personality type's DISC Results defined value.
        /// </summary>
        public Dictionary<string, Guid> DiscProfiles { get; set; } = new Dictionary<string, Guid>();

        /// <summary>
        /// The name of every spiritual gift, conflict mode, motivator, and motivator theme defined value.
        /// </summary>
        public Dictionary<Guid, string> Names { get; set; } = new Dictionary<Guid, string>();

        /// <summary>
        /// Each motivator's and motivator theme's AttributeScoreKey.
        /// </summary>
        public Dictionary<Guid, string> ScoreKeys { get; set; } = new Dictionary<Guid, string>();

        /// <summary>
        /// Each motivator's theme, when it has one.
        /// </summary>
        public Dictionary<Guid, Guid> Themes { get; set; } = new Dictionary<Guid, Guid>();
    }

    /// <summary>
    /// One of Rock's five assessments: how a person answers it, and Rock's scoring.
    /// </summary>
    internal abstract class AssessmentInstrument
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        /// <summary>
        /// The five instruments, by the type keys in configuration/assessments.json.
        /// </summary>
        public static Dictionary<string, AssessmentInstrument> All( AssessmentReference reference )
        {
            var all = new AssessmentInstrument[] { new Disc( reference ), new SpiritualGifts( reference ), new ConflictProfile( reference ), new EmotionalIntelligence(), new Motivators( reference ) };
            return all.ToDictionary( i => i.Key );
        }

        public abstract string Key { get; }

        /// <summary>
        /// The dimensions a leaning can name.
        /// </summary>
        public abstract IReadOnlyList<string> Dimensions { get; }

        /// <summary>
        /// How high in <see cref="AssessmentScore.Ranking"/> a dimension a Cast person leans toward must come out.
        /// </summary>
        public abstract int TopCount { get; }

        /// <summary>
        /// The person attribute that holds the day it was taken, or null.
        /// </summary>
        public virtual string LastSaveDateKey => null;

        /// <summary>
        /// Answers leaning toward the given dimensions: 0 is typical, and each 1 is about one standard deviation more.
        /// </summary>
        public abstract AssessmentAnswers Answer( IReadOnlyDictionary<string, double> leaning, SeededRandom random );

        public abstract AssessmentScore Score( AssessmentAnswers answers );

        /// <summary>
        /// A result taken outside Rock's own assessment, exactly as given, with no answers behind it.
        /// Only DISC has them.
        /// </summary>
        public virtual AssessmentScore Legacy( CastDiscScores scores ) => throw new InvalidDataException( $"Only a DISC result can be a legacy result, not {Key}." );

        /// <summary>
        /// Rock's GetPercentFromScore.
        /// </summary>
        protected static decimal Percent( double z )
        {
            return z > 3.0 ? 100 : z < -3.0 ? 0 : Convert.ToDecimal( RockAssessmentTables.ZScoreToPercentage[z] );
        }

        protected static string Text( decimal value ) => value.ToString( Invariant );

        protected static string GuidList( IEnumerable<Guid> guids ) => string.Join( ",", guids.Select( g => g.ToString() ) );

        protected static double Normal( SeededRandom random )
        {
            return Math.Sqrt( -2 * Math.Log( 1 - random.Between( 0.0, 1.0 ) ) ) * Math.Cos( 2 * Math.PI * random.Between( 0.0, 1.0 ) );
        }

        protected static double Lean( IReadOnlyDictionary<string, double> leaning, string dimension ) => leaning.TryGetValue( dimension, out var lean ) ? lean : 0;

        protected static List<T> Shuffle<T>( IEnumerable<T> items, SeededRandom random )
        {
            var list = items.ToList();
            for ( var i = list.Count - 1; i > 0; i-- )
            {
                var j = random.Between( 0, i );
                (list[i], list[j]) = (list[j], list[i]);
            }

            return list;
        }

        /// <summary>
        /// One scored dimension: its key, Rock's mean and standard deviation of its sum, and its questions.
        /// </summary>
        protected sealed record ScaleDimension( string Key, double Mean, double StandardDeviation, string[] Questions );

        /*
            11/16/26 - CLAUDE

            Each answer starts from Rock's own mean for its dimension, spread over its questions, so a person with no
            lean scores near the 50th percentile. A lean moves every answer of the dimension together, sized so the
            dimension's sum moves about one of Rock's standard deviations per point of lean, and each answer also gets
            its own noise and the person's general tendency to agree.

            Reason: Results spread the way Rock's norms expect, and a lean shows in the result.
        */

        /// <summary>
        /// Answers every question on a scale from <paramref name="min"/> to <paramref name="max"/>, in a shuffled order as
        /// Rock's block shows them.
        /// </summary>
        protected static ScaleAnswers AnswerScale( IReadOnlyList<ScaleDimension> dimensions, IEnumerable<string> questions, int min, int max, IReadOnlyDictionary<string, double> leaning, SeededRandom random )
        {
            var dimensionOf = dimensions.SelectMany( d => d.Questions.Select( q => (Question: q, Dimension: d) ) ).ToDictionary( x => x.Question, x => x.Dimension );
            var noise = 0.25 * ( max - min );
            var tendency = 0.25 * Normal( random );
            var answers = new List<KeyValuePair<string, int>>();
            foreach ( var question in Shuffle( questions, random ) )
            {
                var d = dimensionOf[question];
                var n = d.Questions.Length;
                var gain = Math.Sqrt( Math.Max( d.StandardDeviation * d.StandardDeviation - n * noise * noise, 0.36 * d.StandardDeviation * d.StandardDeviation ) ) / n;
                var value = d.Mean / n + gain * Lean( leaning, d.Key ) + noise * Normal( random ) + tendency;
                answers.Add( new KeyValuePair<string, int>( question, Math.Min( max, Math.Max( min, ( int ) Math.Round( value ) ) ) ) );
            }

            return new ScaleAnswers( answers );
        }

        /// <summary>
        /// A key for a defined value's name, such as "growth-propensity".
        /// </summary>
        protected static string Slug( string name ) => name.Trim().ToLowerInvariant().Replace( ' ', '-' );

        #region JSON as Rock's ToJson writes it

        /// <summary>
        /// Newtonsoft's text for a double: round-trip digits, with ".0" on a whole number.
        /// </summary>
        protected static string JsonDouble( double value )
        {
            // Rock runs on .NET Framework, which writes negative zero as "0".
            var text = ( value == 0 ? 0.0 : value ).ToString( "R", Invariant );
            return text.IndexOf( '.' ) >= 0 || text.IndexOf( 'E' ) >= 0 || text.IndexOf( 'e' ) >= 0 ? text : text + ".0";
        }

        protected static string JsonString( string value )
        {
            var json = new StringBuilder( "\"" );
            foreach ( var c in value )
            {
                switch ( c )
                {
                    case '"':
                        json.Append( "\\\"" );
                        break;
                    case '\\':
                        json.Append( "\\\\" );
                        break;
                    case '\n':
                        json.Append( "\\n" );
                        break;
                    case '\r':
                        json.Append( "\\r" );
                        break;
                    case '\t':
                        json.Append( "\\t" );
                        break;
                    default:
                        if ( c < ' ' )
                        {
                            json.Append( "\\u" ).Append( ( ( int ) c ).ToString( "x4", Invariant ) );
                        }
                        else
                        {
                            json.Append( c );
                        }

                        break;
                }
            }

            return json.Append( '"' ).ToString();
        }

        protected static string JsonObject( IEnumerable<KeyValuePair<string, string>> members )
        {
            return "{" + string.Join( ",", members.Select( m => $"{JsonString( m.Key )}:{m.Value}" ) ) + "}";
        }

        /// <summary>
        /// A dictionary of strings, such as one dimension's answers in Rock's AssessmentData.
        /// </summary>
        protected static string JsonStrings( IEnumerable<KeyValuePair<string, string>> values )
        {
            return JsonObject( values.Select( v => new KeyValuePair<string, string>( v.Key, JsonString( v.Value ) ) ) );
        }

        /// <summary>
        /// Rock's AssessmentResultData shape, { Result, TimeToTake }.
        /// </summary>
        protected static string ResultAndTime( string result, double secondsToTake )
        {
            return JsonObject( new[] { new KeyValuePair<string, string>( "Result", result ), new KeyValuePair<string, string>( "TimeToTake", JsonDouble( secondsToTake ) ) } );
        }

        #endregion

        #region DISC

        /// <summary>
        /// Rock's DiscService and the DISC block's answer data.
        /// </summary>
        private sealed class Disc : AssessmentInstrument
        {
            private static readonly char[] Letters = { 'D', 'I', 'S', 'C' };
            private static readonly Dictionary<string, (double Mean, double StandardDeviation)> Constants = RockAssessmentTables.DiscConstants.ToDictionary( c => c.Key, c => (c.Mean, c.StandardDeviation) );

            /*
                11/16/26 - CLAUDE

                A typical person picks each letter as Most about as often as Rock's adaptive means say, and as Least as
                often as its natural means say, so a person with no lean scores near the 50th percentile. A lean makes a
                letter likelier as Most and less likely as Least.

                Reason: DISC results spread across all sixteen profiles the way Rock's norms expect.
            */
            private static readonly Dictionary<char, double> MostShare = Letters.ToDictionary( l => l, l => ( Constants["Adaptive" + l].Mean - 5 ) / 30 );
            private static readonly Dictionary<char, double> LeastShare = Letters.ToDictionary( l => l, l => ( 20 - Constants["Natural" + l].Mean ) / 30 );
            private const double LeanStrength = 0.6;

            private readonly AssessmentReference _reference;

            public Disc( AssessmentReference reference )
            {
                _reference = reference;
            }

            public override string Key => "disc";

            public override IReadOnlyList<string> Dimensions { get; } = Letters.Select( l => l.ToString() ).ToList();

            public override int TopCount => 2;

            public override string LastSaveDateKey => "LastSaveDate";

            public override AssessmentAnswers Answer( IReadOnlyDictionary<string, double> leaning, SeededRandom random )
            {
                char Pick( IEnumerable<char> letters, Func<char, double> weight )
                {
                    var weighted = letters.Select( l => (Letter: l, Weight: weight( l )) ).ToList();
                    var roll = random.Between( 0.0, weighted.Sum( w => w.Weight ) );
                    foreach ( var w in weighted )
                    {
                        roll -= w.Weight;
                        if ( roll <= 0 )
                        {
                            return w.Letter;
                        }
                    }

                    return weighted[weighted.Count - 1].Letter;
                }

                var questions = new List<(char Most, char Least)>();
                for ( var q = 0; q < RockAssessmentTables.DiscQuestions.GetLength( 0 ); q++ )
                {
                    var letters = RockAssessmentTables.DiscQuestions[q, 4].ToCharArray();
                    var most = Pick( letters, l => MostShare[l] * Math.Exp( LeanStrength * Lean( leaning, l.ToString() ) ) );
                    var least = Pick( letters.Where( l => l != most ), l => LeastShare[l] * Math.Exp( -LeanStrength * Lean( leaning, l.ToString() ) ) );
                    questions.Add( (most, least) );
                }

                return new DiscAnswers( questions );
            }

            public override AssessmentScore Score( AssessmentAnswers answers )
            {
                var disc = ( DiscAnswers ) answers;
                var adaptive = Letters.ToDictionary( l => l, l => AdaptiveScore( l, disc.Questions.Count( q => q.Most == l ) ) );
                var natural = Letters.ToDictionary( l => l, l => NaturalScore( l, disc.Questions.Count( q => q.Least == l ) ) );

                // Rock's DetermineNaturalPersonalityType sorts four items, which List.Sort does with a stable insertion sort.
                var ranked = Letters.OrderByDescending( l => natural[l] ).ToList();
                var personalityType = $"{ranked[0]}{( natural[ranked[1]] > 50 ? ranked[1].ToString() : string.Empty )}";
                var profile = Profile( personalityType );
                var values = Letters.Select( l => new KeyValuePair<string, string>( "Adaptive" + l, adaptive[l].ToString( Invariant ) ) )
                    .Concat( Letters.Select( l => new KeyValuePair<string, string>( "Natural" + l, natural[l].ToString( Invariant ) ) ) )
                    .Append( new KeyValuePair<string, string>( "PersonalityType", personalityType ) )
                    .Append( new KeyValuePair<string, string>( "core_DISCDISCProfile", profile.ToString() ) )
                    .ToList();

                return new AssessmentScore( values, ranked.Select( l => l.ToString() ).ToList(), personalityType, seconds => ResultAndTime( ResultJson( disc ), seconds ) );
            }

            public override AssessmentScore Legacy( CastDiscScores scores )
            {
                var values = Letters.Select( l => new KeyValuePair<string, string>( "Adaptive" + l, scores.Adaptive[l.ToString()].ToString( Invariant ) ) )
                    .Concat( Letters.Select( l => new KeyValuePair<string, string>( "Natural" + l, scores.Natural[l.ToString()].ToString( Invariant ) ) ) )
                    .Append( new KeyValuePair<string, string>( "PersonalityType", scores.PersonalityType ) )
                    .Append( new KeyValuePair<string, string>( "core_DISCDISCProfile", Profile( scores.PersonalityType ).ToString() ) )
                    .ToList();
                return new AssessmentScore( values, scores.PersonalityType.Select( c => c.ToString() ).ToList(), scores.PersonalityType, _ => null );
            }

            private Guid Profile( string personalityType )
            {
                return _reference.DiscProfiles.TryGetValue( personalityType, out var profile )
                    ? profile
                    : throw new InvalidOperationException( $"Rock's DISC Results defined type has no {personalityType} profile." );
            }

            /// <summary>
            /// The DISC block's answer data: each question number's Most and Least, each a letter and its response's text.
            /// </summary>
            private static string ResultJson( DiscAnswers disc )
            {
                string Response( int question, char letter )
                {
                    var letters = RockAssessmentTables.DiscQuestions[question, 4];
                    return $"[{JsonString( letter.ToString() )},{JsonString( RockAssessmentTables.DiscQuestions[question, letters.IndexOf( letter )] )}]";
                }

                return JsonObject( disc.Questions.Select( ( q, i ) => new KeyValuePair<string, string>( ( i + 1 ).ToString( Invariant ),
                    JsonObject( new[] { new KeyValuePair<string, string>( "Most", Response( i, q.Most ) ), new KeyValuePair<string, string>( "Least", Response( i, q.Least ) ) } ) ) ) );
            }

            private static int AdaptiveScore( char letter, int count )
            {
                var constant = Constants["Adaptive" + letter];
                return Convert.ToInt32( Percent( Math.Round( ( count + 5 - constant.Mean ) / constant.StandardDeviation, 1 ) ) );
            }

            private static int NaturalScore( char letter, int count )
            {
                var nb = 30 - count;
                var constant = Constants["Natural" + letter];
                return Convert.ToInt32( Percent( Math.Round( ( nb - 10 - constant.Mean ) / constant.StandardDeviation, 1 ) ) );
            }
        }

        #endregion

        #region Spiritual Gifts

        /// <summary>
        /// Rock's SpiritualGiftsService and the Spiritual Gifts block's answer data. Answers run from 0 (Disagree) to 4 (Strongly Agree).
        /// </summary>
        private sealed class SpiritualGifts : AssessmentInstrument
        {
            private readonly AssessmentReference _reference;
            private readonly List<ScaleDimension> _gifts;

            public SpiritualGifts( AssessmentReference reference )
            {
                _reference = reference;
                _gifts = RockAssessmentTables.SpiritualGifts.Select( g => new ScaleDimension( Slug( reference.Names[g.Gift] ), g.Mean, g.StandardDeviation, g.Questions ) ).ToList();
                Dimensions = _gifts.Select( g => g.Key ).ToList();
            }

            public override string Key => "spiritual-gifts";

            public override IReadOnlyList<string> Dimensions { get; }

            public override int TopCount => 5;

            public override string LastSaveDateKey => "core_SpiritualGiftsLastSaveDate";

            public override AssessmentAnswers Answer( IReadOnlyDictionary<string, double> leaning, SeededRandom random )
            {
                return AnswerScale( _gifts, RockAssessmentTables.SpiritualGiftQuestions, 0, 4, leaning, random );
            }

            public override AssessmentScore Score( AssessmentAnswers answers )
            {
                var responses = ( ( ScaleAnswers ) answers ).Answers;
                var lookup = responses.ToDictionary( a => a.Key, a => a.Value );
                var scores = new List<(Guid Gift, string Key, string Name, double ZScore, double Percentage)>();
                for ( var i = 0; i < RockAssessmentTables.SpiritualGifts.Length; i++ )
                {
                    var gift = RockAssessmentTables.SpiritualGifts[i];
                    double total = 0;
                    foreach ( var question in gift.Questions )
                    {
                        if ( lookup.TryGetValue( question, out var value ) )
                        {
                            total += value;
                        }
                    }

                    var zScore = Math.Round( ( total - gift.Mean ) / gift.StandardDeviation, 1 );
                    RockAssessmentTables.ZScoreToPercentage.TryGetValue( zScore, out var percentage );
                    scores.Add( (gift.Gift, _gifts[i].Key, _reference.Names[gift.Gift], zScore, percentage) );
                }

                var ordered = scores.OrderByDescending( s => s.ZScore ).ToList();
                var dominant = ordered.Take( 5 ).ToList();
                var supportive = ordered.Skip( 5 ).Where( s => s.ZScore >= 0.0 ).ToList();
                var other = ordered.Skip( 5 ).Where( s => s.ZScore < 0.0 ).ToList();
                var values = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>( "core_DominantGifts", GuidList( dominant.Select( s => s.Gift ) ) ),
                    new KeyValuePair<string, string>( "core_SupportiveGifts", GuidList( supportive.Select( s => s.Gift ) ) ),
                    new KeyValuePair<string, string>( "core_OtherGifts", GuidList( other.Select( s => s.Gift ) ) )
                };

                string ResultData( double seconds )
                {
                    var result = JsonObject( responses.Select( a => new KeyValuePair<string, string>( a.Key, a.Value.ToString( Invariant ) ) ) );
                    var resultScores = "[" + string.Join( ",", scores.Select( s => JsonObject( new[]
                    {
                        new KeyValuePair<string, string>( "DefinedValueGuid", JsonString( s.Gift.ToString() ) ),
                        new KeyValuePair<string, string>( "SpiritualGiftName", JsonString( s.Name ) ),
                        new KeyValuePair<string, string>( "ZScore", JsonDouble( s.ZScore ) ),
                        new KeyValuePair<string, string>( "Percentage", JsonDouble( s.Percentage ) )
                    } ) ) ) + "]";
                    return JsonObject( new[]
                    {
                        new KeyValuePair<string, string>( "Result", result ),
                        new KeyValuePair<string, string>( "ResultScores", resultScores ),
                        new KeyValuePair<string, string>( "TimeToTake", JsonDouble( seconds ) )
                    } );
                }

                return new AssessmentScore( values, ordered.Select( s => s.Key ).ToList(), string.Join( ", ", dominant.Take( 3 ).Select( s => s.Name ) ), ResultData );
            }
        }

        #endregion

        #region Conflict Profile

        /// <summary>
        /// Rock's ConflictProfileService and the Conflict Profile block's answer data. Answers run from 1 (Strongly Disagree)
        /// to 4 (Strongly Agree), and each mode scores by its share of all the answers.
        /// </summary>
        private sealed class ConflictProfile : AssessmentInstrument
        {
            private static readonly Guid Winning = new Guid( "56300095-86AD-43FE-98D2-50829E9223C2" );
            private static readonly Guid Resolving = new Guid( "DF7B1EB2-7E7E-4F91-BD26-C6DFD88E38DF" );
            private static readonly Guid Compromising = new Guid( "CF78D6B1-38AA-4FF7-9A4B-E900438FA85A" );
            private static readonly Guid Avoiding = new Guid( "663B0F4A-DE1F-46BE-8BDD-D7C98863DDC4" );
            private static readonly Guid Yielding = new Guid( "4AB06A6F-F5B1-4385-9365-199EA7969E50" );

            private readonly List<ScaleDimension> _modes;
            private readonly Dictionary<Guid, string> _keys;

            public ConflictProfile( AssessmentReference reference )
            {
                _keys = RockAssessmentTables.ConflictModes.ToDictionary( m => m.Mode, m => Slug( reference.Names[m.Mode] ) );
                _modes = RockAssessmentTables.ConflictModes.Select( m => new ScaleDimension( _keys[m.Mode], m.Mean, m.StandardDeviation, m.Questions ) ).ToList();
                Dimensions = _modes.Select( m => m.Key ).ToList();
            }

            public override string Key => "conflict-profile";

            public override IReadOnlyList<string> Dimensions { get; }

            public override int TopCount => 2;

            public override AssessmentAnswers Answer( IReadOnlyDictionary<string, double> leaning, SeededRandom random )
            {
                return AnswerScale( _modes, RockAssessmentTables.ConflictQuestions, 1, 4, leaning, random );
            }

            public override AssessmentScore Score( AssessmentAnswers answers )
            {
                var responses = ( ( ScaleAnswers ) answers ).Answers;
                var lookup = responses.ToDictionary( a => a.Key, a => a.Value );
                var grandTotal = responses.Sum( a => Convert.ToDouble( a.Value ) );
                var zScores = new Dictionary<Guid, double>();
                var data = new Dictionary<Guid, List<KeyValuePair<string, string>>>();
                foreach ( var mode in RockAssessmentTables.ConflictModes )
                {
                    double total = 0;
                    var answered = new List<KeyValuePair<string, string>>();
                    foreach ( var question in mode.Questions )
                    {
                        if ( lookup.TryGetValue( question, out var value ) )
                        {
                            total += value;
                            answered.Add( new KeyValuePair<string, string>( question, value.ToString( Invariant ) ) );
                        }
                    }

                    var relativeProportion = ( total / grandTotal ) * 100;
                    zScores[mode.Mode] = Math.Round( ( relativeProportion - mode.Mean ) / mode.StandardDeviation, 1 );
                    data[mode.Mode] = answered;
                }

                var winning = Percent( zScores[Winning] );
                var resolving = Percent( zScores[Resolving] );
                var compromising = Percent( zScores[Compromising] );
                var avoiding = Percent( zScores[Avoiding] );
                var yielding = Percent( zScores[Yielding] );
                var totalPerc = winning + resolving + compromising + avoiding + yielding;
                var solvingTheme = Math.Round( ( resolving + compromising ) * 100 / totalPerc, 1 );
                var accommodatingTheme = Math.Round( ( avoiding + yielding ) * 100 / totalPerc, 1 );
                var winningTheme = Math.Round( winning * 100 / totalPerc, 1 );

                var values = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>( "core_ConflictModeWinning", Text( winning ) ),
                    new KeyValuePair<string, string>( "core_ConflictModeResolving", Text( resolving ) ),
                    new KeyValuePair<string, string>( "core_ConflictModeCompromising", Text( compromising ) ),
                    new KeyValuePair<string, string>( "core_ConflictModeAvoiding", Text( avoiding ) ),
                    new KeyValuePair<string, string>( "core_ConflictModeYielding", Text( yielding ) ),
                    new KeyValuePair<string, string>( "core_ConflictThemeAccommodating", Text( accommodatingTheme ) ),
                    new KeyValuePair<string, string>( "core_ConflictThemeWinning", Text( winningTheme ) ),
                    new KeyValuePair<string, string>( "core_ConflictThemeSolving", Text( solvingTheme ) )
                };

                // Rock's AssessmentData declares its modes in this order.
                var result = JsonObject( new[] { ("Winning", Winning), ("Avoiding", Avoiding), ("Compromising", Compromising), ("Resolving", Resolving), ("Yielding", Yielding) }
                    .Select( m => new KeyValuePair<string, string>( m.Item1, JsonStrings( data[m.Item2] ) ) ) );

                var scores = new[] { (Winning, winning), (Resolving, resolving), (Compromising, compromising), (Avoiding, avoiding), (Yielding, yielding) };
                var ranking = scores.OrderByDescending( s => s.Item2 ).Select( s => _keys[s.Item1] ).ToList();
                var themes = new[] { ("Solving", solvingTheme), ("Accommodating", accommodatingTheme), ("Winning", winningTheme) };
                return new AssessmentScore( values, ranking, themes.OrderByDescending( t => t.Item2 ).First().Item1, seconds => ResultAndTime( result, seconds ) );
            }
        }

        #endregion

        #region Emotional Intelligence

        /// <summary>
        /// Rock's EQInventoryService and the EQ block's answer data. Answers run from 1 to 5, reversed for questions worded
        /// the other way (their codes end in N), which the block does before it saves them.
        /// </summary>
        private sealed class EmotionalIntelligence : AssessmentInstrument
        {
            /// <summary>
            /// Each scale's person attribute: its key, the key Rock's EQ attributes use, and its name in Rock's AssessmentData.
            /// </summary>
            private static readonly Dictionary<Guid, (string Dimension, string AttributeKey, string DataName)> Scales = new Dictionary<Guid, (string, string, string)>
            {
                [new Guid( "A5EFCE3E-EA41-4FEC-99F6-DD748A7D5BB5" )] = ("self-awareness", "core_EQSelfAwareness", "SelfAware"),
                [new Guid( "149CD0CD-3CD6-44B6-8D84-A17A477A8978" )] = ("self-regulating", "core_EQSelfRegulating", "SelfRegulate"),
                [new Guid( "A6AF0BE5-E93A-49EB-AFEA-3520B7C41C78" )] = ("others-awareness", "core_EQOthersAwareness", "OthersAware"),
                [new Guid( "129C108E-CE61-4DFB-A9A8-1EBC3462022E" )] = ("others-regulating", "core_EQOthersRegulating", "OthersRegulate"),
                [new Guid( "B598BF9C-7A0C-467E-B467-13B40DAC9F8D" )] = ("problem-solving", "core_EQProblemSolving", "EQ_InProblemSolving"),
                [new Guid( "C3CB8FB5-34A2-48C8-B1FC-7CEBA670C1ED" )] = ("under-stress", "core_EQUnderStress", "EQ_UnderStress")
            };

            /// <summary>
            /// Rock's AssessmentData declares its scales in this order.
            /// </summary>
            private static readonly string[] DataOrder = { "SelfAware", "OthersRegulate", "OthersAware", "SelfRegulate", "EQ_InProblemSolving", "EQ_UnderStress" };

            private static readonly List<ScaleDimension> ScaleDimensions = RockAssessmentTables.EqScales.Select( s => new ScaleDimension( Scales[s.Scale].Dimension, s.Mean, s.StandardDeviation, s.Questions ) ).ToList();

            public override string Key => "emotional-intelligence";

            public override IReadOnlyList<string> Dimensions { get; } = ScaleDimensions.Select( d => d.Key ).ToList();

            public override int TopCount => 2;

            public override AssessmentAnswers Answer( IReadOnlyDictionary<string, double> leaning, SeededRandom random )
            {
                return AnswerScale( ScaleDimensions, RockAssessmentTables.EqQuestions, 1, 5, leaning, random );
            }

            public override AssessmentScore Score( AssessmentAnswers answers )
            {
                var lookup = ( ( ScaleAnswers ) answers ).Answers.ToDictionary( a => a.Key, a => a.Value );
                var values = new List<KeyValuePair<string, string>>();
                var data = new Dictionary<string, string>();
                var scores = new List<(string Dimension, decimal Score)>();
                foreach ( var scale in RockAssessmentTables.EqScales )
                {
                    double total = 0;
                    var answered = new List<KeyValuePair<string, string>>();
                    foreach ( var question in scale.Questions )
                    {
                        if ( lookup.TryGetValue( question, out var value ) )
                        {
                            total += value;
                            answered.Add( new KeyValuePair<string, string>( question, value.ToString( Invariant ) ) );
                        }
                    }

                    var info = Scales[scale.Scale];
                    var score = Percent( Math.Round( ( total - scale.Mean ) / scale.StandardDeviation, 1 ) );
                    values.Add( new KeyValuePair<string, string>( info.AttributeKey, Text( score ) ) );
                    data[info.DataName] = JsonStrings( answered );
                    scores.Add( (info.Dimension, score) );
                }

                var result = JsonObject( DataOrder.Select( name => new KeyValuePair<string, string>( name, data[name] ) ) );
                var ranking = scores.OrderByDescending( s => s.Score ).Select( s => s.Dimension ).ToList();
                return new AssessmentScore( values, ranking, $"self-awareness {Text( scores[0].Score )}", seconds => ResultAndTime( result, seconds ) );
            }
        }

        #endregion

        #region Motivators

        /// <summary>
        /// Rock's MotivatorService and the Motivators block's answer data. Answers run from 0 to 6, reversed for questions
        /// worded the other way (their codes end in N), which the block does before it saves them.
        /// </summary>
        private sealed class Motivators : AssessmentInstrument
        {
            private readonly AssessmentReference _reference;
            private readonly List<ScaleDimension> _motivators;

            public Motivators( AssessmentReference reference )
            {
                _reference = reference;
                var questions = RockAssessmentTables.MotivatorQuestions.ToLookup( q => q.Motivator, q => q.Code );
                _motivators = RockAssessmentTables.Motivators.Select( m => new ScaleDimension( Slug( reference.Names[m.Motivator] ), m.Mean, m.StandardDeviation, questions[m.Motivator].ToArray() ) ).ToList();
                Dimensions = _motivators.Select( m => m.Key ).ToList();
            }

            public override string Key => "motivators";

            public override IReadOnlyList<string> Dimensions { get; }

            public override int TopCount => 5;

            public override AssessmentAnswers Answer( IReadOnlyDictionary<string, double> leaning, SeededRandom random )
            {
                return AnswerScale( _motivators, RockAssessmentTables.MotivatorQuestions.Select( q => q.Code ), 0, 6, leaning, random );
            }

            public override AssessmentScore Score( AssessmentAnswers answers )
            {
                var lookup = ( ( ScaleAnswers ) answers ).Answers.ToDictionary( a => a.Key, a => a.Value );
                var scores = new List<(Guid Motivator, string Key, decimal Score)>();
                var data = new List<KeyValuePair<string, string>>();
                for ( var i = 0; i < RockAssessmentTables.Motivators.Length; i++ )
                {
                    var motivator = RockAssessmentTables.Motivators[i];
                    double total = 0;
                    var answered = new List<KeyValuePair<string, string>>();
                    foreach ( var question in RockAssessmentTables.MotivatorQuestions.Where( q => q.Motivator == motivator.Motivator ) )
                    {
                        if ( lookup.TryGetValue( question.Code, out var value ) )
                        {
                            total += value;
                            answered.Add( new KeyValuePair<string, string>( question.Code, value.ToString( Invariant ) ) );
                        }
                    }

                    scores.Add( (motivator.Motivator, _motivators[i].Key, Percent( Math.Round( ( total - motivator.Mean ) / motivator.StandardDeviation, 1 ) )) );
                    data.Add( new KeyValuePair<string, string>( _reference.Names[motivator.Motivator], JsonStrings( answered ) ) );
                }

                var ranked = scores.OrderByDescending( s => s.Score ).ToList();
                var themeScores = new Dictionary<Guid, List<decimal>>();
                foreach ( var score in ranked )
                {
                    if ( _reference.Themes.TryGetValue( score.Motivator, out var theme ) )
                    {
                        if ( !themeScores.ContainsKey( theme ) )
                        {
                            themeScores[theme] = new List<decimal>();
                        }

                        themeScores[theme].Add( score.Score );
                    }
                }

                var values = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>( "core_MotivatorsTop5Motivators", GuidList( ranked.Take( 5 ).Select( s => s.Motivator ) ) ) };
                values.AddRange( ranked.Select( s => new KeyValuePair<string, string>( _reference.ScoreKeys[s.Motivator], Text( s.Score ) ) ) );
                values.AddRange( themeScores.Select( t => new KeyValuePair<string, string>( _reference.ScoreKeys[t.Key], Text( t.Value.Sum() / t.Value.Count() ) ) ) );

                var result = JsonObject( data );
                return new AssessmentScore( values, ranked.Select( s => s.Key ).ToList(), string.Join( ", ", ranked.Take( 3 ).Select( s => _reference.Names[s.Motivator] ) ), seconds => ResultAndTime( result, seconds ) );
            }
        }

        #endregion
    }
}

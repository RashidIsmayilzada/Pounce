using System.Globalization;

namespace Pounce.Core;

/// <summary>
/// Case- and diacritic-insensitive text for matching names ("Café" and "cafe" match), the same on every machine.
/// </summary>
/// <remarks>
/// <see cref="string.Normalize()"/> can't be used: it needs ICU or NLS, and under invariant globalization (the tests,
/// for one) it silently changes nothing. So the canonical decompositions of the accented letters in the Latin, Greek
/// and Cyrillic blocks are built in: each maps to its base letter. Combining marks are dropped and the rest is
/// lower-cased with the invariant (culture-free) rules.
/// </remarks>
internal static class TextFolding
{
    private const char First = '\u00C0';

    private static readonly char[] Bases = BuildBases();

    /// <summary>The text without accents, in lower case, for ordinal comparison.</summary>
    public static string Fold(string text)
    {
        Span<char> buffer = text.Length <= 256 ? stackalloc char[text.Length] : new char[text.Length];
        int length = 0;
        foreach (char c in text)
        {
            char letter = c >= First && c - First < Bases.Length && Bases[c - First] != '\0' ? Bases[c - First] : c;
            if (CharUnicodeInfo.GetUnicodeCategory(letter) != UnicodeCategory.NonSpacingMark)
            {
                buffer[length++] = char.ToLowerInvariant(letter);
            }
        }
        return new string(buffer[..length]);
    }

    private static char[] BuildBases()
    {
        int last = 0;
        for (int i = 0; i < Pairs.Length; i += 2)
        {
            last = Math.Max(last, Pairs[i]);
        }
        char[] bases = new char[last - First + 1];
        for (int i = 0; i < Pairs.Length; i += 2)
        {
            bases[Pairs[i] - First] = Pairs[i + 1];
        }
        return bases;
    }

    // Pairs of (accented letter, base letter): every letter in U+00C0-U+024F, U+0370-U+04FF and U+1E00-U+1FFF whose
    // canonical decomposition is a letter followed only by combining marks. Generated with Python 3 (Unicode 16.0):
    //
    //   for cp in chain(range(0xC0, 0x250), range(0x370, 0x500), range(0x1E00, 0x2000)):
    //       d = unicodedata.normalize("NFD", chr(cp))
    //       if len(d) > 1 and category(d[0])[0] == "L" and all(category(m) == "Mn" for m in d[1:]):
    //           emit(chr(cp) + d[0])
    private const string Pairs =
        "ÀAÁAÂAÃAÄAÅAÇCÈEÉEÊEËEÌIÍIÎIÏIÑNÒOÓOÔOÕOÖOÙUÚUÛU" +
        "ÜUÝYàaáaâaãaäaåaçcèeéeêeëeìiíiîiïiñnòoóoôoõoöoùu" +
        "úuûuüuýyÿyĀAāaĂAăaĄAąaĆCćcĈCĉcĊCċcČCčcĎDďdĒEēeĔE" +
        "ĕeĖEėeĘEęeĚEěeĜGĝgĞGğgĠGġgĢGģgĤHĥhĨIĩiĪIīiĬIĭiĮI" +
        "įiİIĴJĵjĶKķkĹLĺlĻLļlĽLľlŃNńnŅNņnŇNňnŌOōoŎOŏoŐOőo" +
        "ŔRŕrŖRŗrŘRřrŚSśsŜSŝsŞSşsŠSšsŢTţtŤTťtŨUũuŪUūuŬUŭu" +
        "ŮUůuŰUűuŲUųuŴWŵwŶYŷyŸYŹZźzŻZżzŽZžzƠOơoƯUưuǍAǎaǏI" +
        "ǐiǑOǒoǓUǔuǕUǖuǗUǘuǙUǚuǛUǜuǞAǟaǠAǡaǢÆǣæǦGǧgǨKǩkǪO" +
        "ǫoǬOǭoǮƷǯʒǰjǴGǵgǸNǹnǺAǻaǼÆǽæǾØǿøȀAȁaȂAȃaȄEȅeȆEȇe" +
        "ȈIȉiȊIȋiȌOȍoȎOȏoȐRȑrȒRȓrȔUȕuȖUȗuȘSșsȚTțtȞHȟhȦAȧa" +
        "ȨEȩeȪOȫoȬOȭoȮOȯoȰOȱoȲYȳyΆΑΈΕΉΗΊΙΌΟΎΥΏΩΐιΪΙΫΥάαέε" +
        "ήηίιΰυϊιϋυόούυώωϓϒϔϒЀЕЁЕЃГЇІЌКЍИЎУЙИйиѐеёеѓгїіќк" +
        "ѝиўуѶѴѷѵӁЖӂжӐАӑаӒАӓаӖЕӗеӚӘӛәӜЖӝжӞЗӟзӢИӣиӤИӥиӦОӧо" +
        "ӪӨӫөӬЭӭэӮУӯуӰУӱуӲУӳуӴЧӵчӸЫӹыḀAḁaḂBḃbḄBḅbḆBḇbḈCḉc" +
        "ḊDḋdḌDḍdḎDḏdḐDḑdḒDḓdḔEḕeḖEḗeḘEḙeḚEḛeḜEḝeḞFḟfḠGḡg" +
        "ḢHḣhḤHḥhḦHḧhḨHḩhḪHḫhḬIḭiḮIḯiḰKḱkḲKḳkḴKḵkḶLḷlḸLḹl" +
        "ḺLḻlḼLḽlḾMḿmṀMṁmṂMṃmṄNṅnṆNṇnṈNṉnṊNṋnṌOṍoṎOṏoṐOṑo" +
        "ṒOṓoṔPṕpṖPṗpṘRṙrṚRṛrṜRṝrṞRṟrṠSṡsṢSṣsṤSṥsṦSṧsṨSṩs" +
        "ṪTṫtṬTṭtṮTṯtṰTṱtṲUṳuṴUṵuṶUṷuṸUṹuṺUṻuṼVṽvṾVṿvẀWẁw" +
        "ẂWẃwẄWẅwẆWẇwẈWẉwẊXẋxẌXẍxẎYẏyẐZẑzẒZẓzẔZẕzẖhẗtẘwẙy" +
        "ẛſẠAạaẢAảaẤAấaẦAầaẨAẩaẪAẫaẬAậaẮAắaẰAằaẲAẳaẴAẵaẶA" +
        "ặaẸEẹeẺEẻeẼEẽeẾEếeỀEềeỂEểeỄEễeỆEệeỈIỉiỊIịiỌOọoỎO" +
        "ỏoỐOốoỒOồoỔOổoỖOỗoỘOộoỚOớoỜOờoỞOởoỠOỡoỢOợoỤUụuỦU" +
        "ủuỨUứuỪUừuỬUửuỮUữuỰUựuỲYỳyỴYỵyỶYỷyỸYỹyἀαἁαἂαἃαἄα" +
        "ἅαἆαἇαἈΑἉΑἊΑἋΑἌΑἍΑἎΑἏΑἐεἑεἒεἓεἔεἕεἘΕἙΕἚΕἛΕἜΕἝΕἠη" +
        "ἡηἢηἣηἤηἥηἦηἧηἨΗἩΗἪΗἫΗἬΗἭΗἮΗἯΗἰιἱιἲιἳιἴιἵιἶιἷιἸΙ" +
        "ἹΙἺΙἻΙἼΙἽΙἾΙἿΙὀοὁοὂοὃοὄοὅοὈΟὉΟὊΟὋΟὌΟὍΟὐυὑυὒυὓυὔυ" +
        "ὕυὖυὗυὙΥὛΥὝΥὟΥὠωὡωὢωὣωὤωὥωὦωὧωὨΩὩΩὪΩὫΩὬΩὭΩὮΩὯΩὰα" +
        "άαὲεέεὴηήηὶιίιὸοόοὺυύυὼωώωᾀαᾁαᾂαᾃαᾄαᾅαᾆαᾇαᾈΑᾉΑᾊΑ" +
        "ᾋΑᾌΑᾍΑᾎΑᾏΑᾐηᾑηᾒηᾓηᾔηᾕηᾖηᾗηᾘΗᾙΗᾚΗᾛΗᾜΗᾝΗᾞΗᾟΗᾠωᾡωᾢω" +
        "ᾣωᾤωᾥωᾦωᾧωᾨΩᾩΩᾪΩᾫΩᾬΩᾭΩᾮΩᾯΩᾰαᾱαᾲαᾳαᾴαᾶαᾷαᾸΑᾹΑᾺΑΆΑ" +
        "ᾼΑῂηῃηῄηῆηῇηῈΕΈΕῊΗΉΗῌΗῐιῑιῒιΐιῖιῗιῘΙῙΙῚΙΊΙῠυῡυῢυ" +
        "ΰυῤρῥρῦυῧυῨΥῩΥῪΥΎΥῬΡῲωῳωῴωῶωῷωῸΟΌΟῺΩΏΩῼΩ";
}

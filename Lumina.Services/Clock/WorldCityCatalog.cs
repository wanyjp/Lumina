namespace Lumina.Services.Clock;

/// <summary>世界时钟城市目录项：时区 ID、展示名（城市 + 国家）与排序键（拼音）。</summary>
public sealed record WorldCityOption(string IanaId, string DisplayName, string SortKey);

/// <summary>
/// 内置城市目录（全球覆盖）：一个国家只有一个时区时取首都，存在时差的国家
/// 取代表性城市（美国、加拿大、俄罗斯、澳大利亚、巴西、墨西哥、印度尼西亚）。
/// 展示名统一为「城市（国家）」，按 <see cref="WorldCityOption.SortKey"/> 首字母排序。
/// </summary>
public static class WorldCityCatalog
{
    /// <summary>首启预设城市，与数据库迁移 v2 的种子保持一致。</summary>
    public static IReadOnlyList<WorldCityOption> Presets { get; } = new[]
    {
        new WorldCityOption("America/New_York", "纽约", "niuyue"),
        new WorldCityOption("Europe/London", "伦敦", "lundun"),
        new WorldCityOption("Asia/Tokyo", "东京", "tokyo"),
        new WorldCityOption("Australia/Sydney", "悉尼", "xini"),
        new WorldCityOption("Europe/Paris", "巴黎", "bali"),
        new WorldCityOption("Asia/Dubai", "迪拜", "dibai"),
    };

    public static IReadOnlyList<WorldCityOption> All =>
        _all ??= Entries
            .OrderBy(option => option.SortKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<WorldCityOption>? _all;

    private static readonly WorldCityOption[] Entries =
    [
        // 亚洲
        Option("Asia/Shanghai", "北京（中国）", "beijing"),
        Option("Asia/Tokyo", "东京（日本）", "tokyo"),
        Option("Asia/Seoul", "首尔（韩国）", "shouer"),
        Option("Asia/Pyongyang", "平壤（朝鲜）", "pingrang"),
        Option("Asia/Ulaanbaatar", "乌兰巴托（蒙古）", "wulanbatuo"),
        Option("Asia/Kolkata", "新德里（印度）", "xindeli"),
        Option("Asia/Kathmandu", "加德满都（尼泊尔）", "jiamandu"),
        Option("Asia/Dhaka", "达卡（孟加拉国）", "daka"),
        Option("Asia/Colombo", "科伦坡（斯里兰卡）", "kelunpo"),
        Option("Asia/Karachi", "伊斯兰堡（巴基斯坦）", "yisilanbao"),
        Option("Asia/Kabul", "喀布尔（阿富汗）", "kabuer"),
        Option("Asia/Tehran", "德黑兰（伊朗）", "deheilan"),
        Option("Asia/Baghdad", "巴格达（伊拉克）", "bageda"),
        Option("Asia/Riyadh", "利雅得（沙特阿拉伯）", "liyade"),
        Option("Asia/Dubai", "迪拜（阿联酋）", "dibai"),
        Option("Asia/Qatar", "多哈（卡塔尔）", "duoha"),
        Option("Asia/Jerusalem", "耶路撒冷（以色列）", "yelusaleng"),
        Option("Asia/Beirut", "贝鲁特（黎巴嫩）", "beilute"),
        Option("Asia/Amman", "安曼（约旦）", "anman"),
        Option("Asia/Kuwait", "科威特城（科威特）", "keweitecheng"),
        Option("Asia/Muscat", "马斯喀特（阿曼）", "masikate"),
        Option("Asia/Bangkok", "曼谷（泰国）", "mangu"),
        Option("Asia/Ho_Chi_Minh", "河内（越南）", "henei"),
        Option("Asia/Phnom_Penh", "金边（柬埔寨）", "jinbian"),
        Option("Asia/Vientiane", "万象（老挝）", "wanxiang"),
        Option("Asia/Kuala_Lumpur", "吉隆坡（马来西亚）", "jilongpo"),
        Option("Asia/Singapore", "新加坡（新加坡）", "xinjiapo"),
        Option("Asia/Jakarta", "雅加达（印度尼西亚）", "yajiada"),
        Option("Asia/Jayapura", "查亚普拉（印度尼西亚）", "chayapula"),
        Option("Asia/Manila", "马尼拉（菲律宾）", "manila"),
        Option("Asia/Brunei", "斯里巴加湾（文莱）", "silibajiawan"),
        Option("Asia/Dili", "帝力（东帝汶）", "dili"),
        Option("Asia/Tashkent", "塔什干（乌兹别克斯坦）", "tashigan"),
        Option("Asia/Almaty", "阿斯塔纳（哈萨克斯坦）", "asitana"),
        Option("Asia/Bishkek", "比什凯克（吉尔吉斯斯坦）", "bishikaike"),
        Option("Asia/Dushanbe", "杜尚别（塔吉克斯坦）", "dushangbie"),
        Option("Asia/Ashgabat", "阿什哈巴德（土库曼斯坦）", "ashihabade"),
        Option("Asia/Baku", "巴库（阿塞拜疆）", "baku"),
        Option("Asia/Tbilisi", "第比利斯（格鲁吉亚）", "dibilisi"),
        Option("Asia/Yerevan", "埃里温（亚美尼亚）", "ailiewen"),
        Option("Europe/Istanbul", "安卡拉（土耳其）", "ankala"),
        Option("Asia/Nicosia", "尼科西亚（塞浦路斯）", "nikexiya"),

        // 欧洲
        Option("Europe/London", "伦敦（英国）", "lundun"),
        Option("Europe/Paris", "巴黎（法国）", "bali"),
        Option("Europe/Berlin", "柏林（德国）", "bolin"),
        Option("Europe/Rome", "罗马（意大利）", "luoma"),
        Option("Europe/Madrid", "马德里（西班牙）", "madeli"),
        Option("Europe/Lisbon", "里斯本（葡萄牙）", "lisiben"),
        Option("Europe/Amsterdam", "阿姆斯特丹（荷兰）", "amusitedan"),
        Option("Europe/Brussels", "布鲁塞尔（比利时）", "bulusaier"),
        Option("Europe/Vienna", "维也纳（奥地利）", "weiyena"),
        Option("Europe/Zurich", "苏黎世（瑞士）", "sulishi"),
        Option("Europe/Stockholm", "斯德哥尔摩（瑞典）", "sidegeermo"),
        Option("Europe/Oslo", "奥斯陆（挪威）", "aoshilu"),
        Option("Europe/Copenhagen", "哥本哈根（丹麦）", "gebenhagen"),
        Option("Europe/Helsinki", "赫尔辛基（芬兰）", "heerxinji"),
        Option("Atlantic/Reykjavik", "雷克雅未克（冰岛）", "leikeyaweike"),
        Option("Europe/Dublin", "都柏林（爱尔兰）", "dubolin"),
        Option("Europe/Warsaw", "华沙（波兰）", "huasha"),
        Option("Europe/Prague", "布拉格（捷克）", "bulage"),
        Option("Europe/Budapest", "布达佩斯（匈牙利）", "budapeisi"),
        Option("Europe/Athens", "雅典（希腊）", "yadian"),
        Option("Europe/Moscow", "莫斯科（俄罗斯）", "mosike"),
        Option("Asia/Yekaterinburg", "叶卡捷琳堡（俄罗斯）", "yekajielinbao"),
        Option("Asia/Vladivostok", "符拉迪沃斯托克（俄罗斯）", "fuladiwosituoke"),
        Option("Europe/Kyiv", "基辅（乌克兰）", "jifu"),
        Option("Europe/Minsk", "明斯克（白俄罗斯）", "mingsike"),
        Option("Europe/Bucharest", "布加勒斯特（罗马尼亚）", "bujialesite"),
        Option("Europe/Sofia", "索非亚（保加利亚）", "suofeiya"),
        Option("Europe/Belgrade", "贝尔格莱德（塞尔维亚）", "beiergelaide"),
        Option("Europe/Zagreb", "萨格勒布（克罗地亚）", "sagelebu"),
        Option("Europe/Ljubljana", "卢布尔雅那（斯洛文尼亚）", "lubueryana"),
        Option("Europe/Sarajevo", "萨拉热窝（波黑）", "salarewo"),
        Option("Europe/Skopje", "斯科普里（北马其顿）", "sikepuli"),
        Option("Europe/Tirane", "地拉那（阿尔巴尼亚）", "dilana"),
        Option("Europe/Tallinn", "塔林（爱沙尼亚）", "talin"),
        Option("Europe/Riga", "里加（拉脱维亚）", "lijia"),
        Option("Europe/Vilnius", "维尔纽斯（立陶宛）", "weierniusi"),
        Option("Europe/Chisinau", "基希讷乌（摩尔多瓦）", "jixinewu"),
        Option("Europe/Monaco", "摩纳哥（摩纳哥）", "monage"),
        Option("Europe/Malta", "瓦莱塔（马耳他）", "walaida"),
        Option("Europe/Luxembourg", "卢森堡（卢森堡）", "lusenbao"),
        Option("Europe/Andorra", "安道尔城（安道尔）", "andoercheng"),

        // 非洲
        Option("Africa/Cairo", "开罗（埃及）", "kailuo"),
        Option("Africa/Tripoli", "的黎波里（利比亚）", "deliboli"),
        Option("Africa/Tunis", "突尼斯（突尼斯）", "tunisi"),
        Option("Africa/Algiers", "阿尔及尔（阿尔及利亚）", "aierjier"),
        Option("Africa/Casablanca", "拉巴特（摩洛哥）", "labate"),
        Option("Africa/Dakar", "达喀尔（塞内加尔）", "dakaer"),
        Option("Africa/Accra", "阿克拉（加纳）", "akela"),
        Option("Africa/Lagos", "拉各斯（尼日利亚）", "lagesi"),
        Option("Africa/Nairobi", "内罗毕（肯尼亚）", "neiluobi"),
        Option("Africa/Addis_Ababa", "亚的斯亚贝巴（埃塞俄比亚）", "yadisiyabeiba"),
        Option("Africa/Khartoum", "喀土穆（苏丹）", "katumu"),
        Option("Africa/Kinshasa", "金沙萨（刚果金）", "jinshasa"),
        Option("Africa/Luanda", "罗安达（安哥拉）", "luoanda"),
        Option("Africa/Harare", "哈拉雷（津巴布韦）", "halalei"),
        Option("Africa/Lusaka", "卢萨卡（赞比亚）", "lusaka"),
        Option("Africa/Dar_es_Salaam", "达累斯萨拉姆（坦桑尼亚）", "daleisisalamu"),
        Option("Africa/Kampala", "坎帕拉（乌干达）", "kanpala"),
        Option("Africa/Johannesburg", "约翰内斯堡（南非）", "yuehanneisibao"),
        Option("Indian/Antananarivo", "塔那那利佛（马达加斯加）", "tananalifo"),
        Option("Indian/Mauritius", "路易港（毛里求斯）", "luyigang"),

        // 北美洲
        Option("America/New_York", "纽约（美国）", "niuyue"),
        Option("America/Chicago", "芝加哥（美国）", "zhijiage"),
        Option("America/Denver", "丹佛（美国）", "danfo"),
        Option("America/Los_Angeles", "洛杉矶（美国）", "luoshanji"),
        Option("America/Anchorage", "安克雷奇（美国）", "ankeleqi"),
        Option("Pacific/Honolulu", "檀香山（美国）", "tanxiangshan"),
        Option("America/Toronto", "渥太华（加拿大）", "wotaihua"),
        Option("America/Vancouver", "温哥华（加拿大）", "wengehua"),
        Option("America/Whitehorse", "白马市（加拿大）", "baimashi"),
        Option("America/Mexico_City", "墨西哥城（墨西哥）", "moxigecheng"),
        Option("America/Tijuana", "蒂华纳（墨西哥）", "tihuana"),
        Option("America/Guatemala", "危地马拉城（危地马拉）", "weidimalacheng"),
        Option("America/El_Salvador", "圣萨尔瓦多（萨尔瓦多）", "shengsaerwaduo"),
        Option("America/Tegucigalpa", "特古西加尔巴（洪都拉斯）", "teguxijiaerba"),
        Option("America/Managua", "马那瓜（尼加拉瓜）", "managua"),
        Option("America/Costa_Rica", "圣何塞（哥斯达黎加）", "shenghesai"),
        Option("America/Panama", "巴拿马城（巴拿马）", "banamacheng"),
        Option("America/Havana", "哈瓦那（古巴）", "hawana"),
        Option("America/Jamaica", "金斯敦（牙买加）", "jinsidun"),
        Option("America/Santo_Domingo", "圣多明各（多米尼加）", "shengduomingge"),
        Option("America/Port-au-Prince", "太子港（海地）", "taizigang"),
        Option("America/Nassau", "拿骚（巴哈马）", "nasao"),
        Option("America/Port_of_Spain", "西班牙港（特立尼达和多巴哥）", "xibanyagang"),
        Option("America/Barbados", "布里奇敦（巴巴多斯）", "buliqidun"),

        // 南美洲
        Option("America/Sao_Paulo", "圣保罗（巴西）", "shengbaoluo"),
        Option("America/Manaus", "马瑙斯（巴西）", "manaosi"),
        Option("America/Rio_Branco", "里奥布兰科（巴西）", "liaobulanke"),
        Option("America/Argentina/Buenos_Aires", "布宜诺斯艾利斯（阿根廷）", "buyinuosiailisi"),
        Option("America/Santiago", "圣地亚哥（智利）", "shengdiyage"),
        Option("America/Lima", "利马（秘鲁）", "lima"),
        Option("America/Bogota", "波哥大（哥伦比亚）", "bogeda"),
        Option("America/Caracas", "加拉加斯（委内瑞拉）", "jialajiasi"),
        Option("America/Guayaquil", "基多（厄瓜多尔）", "jiduo"),
        Option("America/La_Paz", "拉巴斯（玻利维亚）", "labasi"),
        Option("America/Asuncion", "亚松森（巴拉圭）", "yasongsen"),
        Option("America/Montevideo", "蒙得维的亚（乌拉圭）", "mengdeweideya"),
        Option("America/Guyana", "乔治敦（圭亚那）", "qiaozhidun"),
        Option("America/Paramaribo", "帕拉马里博（苏里南）", "palamalibo"),
        Option("America/Cayenne", "卡宴（法属圭亚那）", "kayan"),

        // 大洋洲
        Option("Australia/Sydney", "悉尼（澳大利亚）", "xini"),
        Option("Australia/Perth", "珀斯（澳大利亚）", "posi"),
        Option("Australia/Darwin", "达尔文（澳大利亚）", "daerwen"),
        Option("Pacific/Auckland", "惠灵顿（新西兰）", "huilingdun"),
        Option("Pacific/Port_Moresby", "莫尔兹比港（巴布亚新几内亚）", "moerzibigang"),
        Option("Pacific/Fiji", "苏瓦（斐济）", "suwa"),
        Option("Pacific/Noumea", "努美阿（新喀里多尼亚）", "numeia"),
        Option("Pacific/Tahiti", "帕皮提（法属波利尼西亚）", "papiti"),
        Option("Pacific/Apia", "阿皮亚（萨摩亚）", "apiya"),
        Option("Pacific/Tongatapu", "努库阿洛法（汤加）", "nukualuofa"),
        Option("Pacific/Tarawa", "塔拉瓦（基里巴斯）", "talawa"),
        Option("Pacific/Majuro", "马朱罗（马绍尔群岛）", "mazhuluo"),
        Option("Pacific/Pohnpei", "帕利基尔（密克罗尼西亚）", "palijier"),
        Option("Pacific/Palau", "梅莱凯奥克（帕劳）", "meilaikaiaoke"),
        Option("Pacific/Efate", "维拉港（瓦努阿图）", "weilagang"),
        Option("Pacific/Guadalcanal", "霍尼亚拉（所罗门群岛）", "huoniyala"),

        // 固定偏移（按“东八区/东六区”选择，覆盖新疆、西藏等实际时差）
        Option("UTC+00:00", "零时区", "lingshiqu"),
        Option("UTC+05:00", "东五区", "dongwuqu"),
        Option("UTC+06:00", "东六区", "dongliuqu"),
        Option("UTC+07:00", "东七区", "dongqiqu"),
        Option("UTC+08:00", "东八区", "dongbaqu"),
        Option("UTC+09:00", "东九区", "dongjiuqu"),
    ];

    private static WorldCityOption Option(string ianaId, string displayName, string sortKey) =>
        new(ianaId, displayName, sortKey);
}

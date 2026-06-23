namespace OdataQueryLite.EFCore.Tests
{
    /// <summary>
    /// The differential corpus. GROUP B = real-world shapes (depth &lt;= 1). GROUP A = single-feature
    /// enumeration of every operator/function/option the engine claims, plus representative
    /// combinations (NOT a full cross-product). Every case is expressed only against the synthetic
    /// model. Gap cases (spec lists it, engine lacks it) are KEPT and flagged, never dropped or fixed.
    /// </summary>
    public static class Corpus
    {
        public static IReadOnlyList<CorpusCase> All { get; } = BuildB().Concat(BuildA()).ToList();

        public static IReadOnlyList<CorpusCase> GroupB => All.Where(c => c.Group == "B").ToList();
        public static IReadOnlyList<CorpusCase> GroupA => All.Where(c => c.Group == "A").ToList();

        // ── GROUP B — real-world, depth <= 1 ───────────────────────────────────────────────────
        private static IEnumerable<CorpusCase> BuildB()
        {
            CorpusCase B(string label, string? filter = null, string? orderBy = null, string? expand = null,
                string? select = null, int? top = null, int? skip = null, bool count = false)
                => new() { Label = "B_" + label, Group = "B", Filter = filter, OrderBy = orderBy, Expand = expand, Select = select, Top = top, Skip = skip, Count = count };

            // string ops
            yield return B("contains_name", filter: "contains(Name, 'get')");
            yield return B("eq_string", filter: "Code eq 'AAA-001'");
            yield return B("ne_string", filter: "Name ne 'Widget'");
            // numeric comparisons
            yield return B("gt_quantity", filter: "Quantity gt 50");
            yield return B("ge_price", filter: "Price ge 19.99");
            yield return B("lt_quantity", filter: "Quantity lt 50");
            yield return B("le_price", filter: "Price le 49.95");
            // numeric range (ge + le)
            yield return B("range_quantity", filter: "Quantity ge 10 and Quantity le 250");
            // date ranges
            yield return B("date_range_ge_lt", filter: "CreatedTime ge 2024-01-01T00:00:00Z and CreatedTime lt 2025-01-01T00:00:00Z");
            yield return B("date_range_ge_le", filter: "CreatedTime ge 2024-01-01T00:00:00Z and CreatedTime le 2024-12-31T23:59:59Z");
            // enum + bool + nullable
            yield return B("eq_enum", filter: "Status eq 'Active'");
            yield return B("eq_bool", filter: "IsActive eq true");
            yield return B("nullable_eq_null", filter: "ClosedTime eq null");
            yield return B("nullable_ne_null", filter: "IsArchived ne null");
            // and-chain mixing kinds
            yield return B("and_chain_mixed", filter: "IsActive eq true and Quantity gt 5 and Status eq 'Active'");
            // 1-level nested ref filter
            yield return B("nested_ref_eq", filter: "Category/Name eq 'Alpha'");
            // orderby single asc/desc
            yield return B("orderby_price_asc", orderBy: "Price asc");
            yield return B("orderby_quantity_desc", orderBy: "Quantity desc");
            // orderby multi-field mixed
            yield return B("orderby_multi_mixed", orderBy: "Status asc, Price desc");
            // top / skip / count
            // $top/$skip need a stable $orderby to be deterministic — without one the DB returns an
            // arbitrary order, so engine vs oracle pick different rows (real data-grids always sort).
            yield return B("top_2", top: 2, orderBy: "Id asc");
            yield return B("skip_2", skip: 2, orderBy: "Id asc");
            yield return B("top_skip", top: 2, skip: 1, orderBy: "Id asc");
            yield return B("count_true", count: true);
            // select flat
            yield return B("select_flat", select: "Id,Name,Price");
            // expand ref-nav with nested select
            yield return B("expand_ref_nested_select", expand: "Category($select=Name,Kind)");
            // expand collection-nav with select
            yield return B("expand_collection_select", expand: "Tags($select=Label,Value)");
        }

        // ── GROUP A — capability full-set ──────────────────────────────────────────────────────
        private static IEnumerable<CorpusCase> BuildA()
        {
            CorpusCase A(string label, string? filter = null, string? orderBy = null, string? expand = null,
                string? select = null, int? top = null, int? skip = null, bool count = false,
                string? apply = null, ExpectKind expect = ExpectKind.Rows, string? note = null)
                => new() { Label = "A_" + label, Group = "A", Filter = filter, OrderBy = orderBy, Expand = expand, Select = select, Top = top, Skip = skip, Count = count, Apply = apply, Expect = expect, Note = note };

            // ─ comparison operators, one per op, over multiple types ─
            yield return A("eq_num", filter: "Quantity eq 10");
            yield return A("ne_num", filter: "Quantity ne 10");
            yield return A("gt_num", filter: "Quantity gt 10");
            yield return A("ge_num", filter: "Quantity ge 10");
            yield return A("lt_num", filter: "Quantity lt 10");
            yield return A("le_num", filter: "Quantity le 10");
            yield return A("eq_decimal", filter: "Price eq 19.99");
            yield return A("gt_decimal", filter: "Price gt 19.99");
            yield return A("eq_str", filter: "Name eq 'Widget'");
            yield return A("ne_str", filter: "Name ne 'Widget'");
            yield return A("eq_date", filter: "CreatedTime eq 2024-01-15T09:30:00Z");
            yield return A("gt_date", filter: "CreatedTime gt 2024-01-15T09:30:00Z");
            yield return A("lt_date", filter: "CreatedTime lt 2024-01-15T09:30:00Z");
            yield return A("eq_bool_true", filter: "IsActive eq true");
            yield return A("eq_bool_false", filter: "IsActive eq false");
            yield return A("ne_bool", filter: "IsActive ne true");
            yield return A("eq_enum_active", filter: "Status eq 'Active'");
            yield return A("eq_enum_pending", filter: "Status eq 'Pending'");
            yield return A("eq_enum_closed", filter: "Status eq 'Closed'");
            yield return A("ne_enum", filter: "Status ne 'Closed'");
            yield return A("eq_nullable_enum", filter: "Priority eq 'High'");
            yield return A("eq_nullable_enum_null", filter: "Priority eq null");
            yield return A("ne_nullable_enum_null", filter: "Priority ne null");

            // ─ nullable scalar null/non-null on each nullable field ─
            yield return A("isarchived_eq_null", filter: "IsArchived eq null");
            yield return A("isarchived_ne_null", filter: "IsArchived ne null");
            yield return A("isarchived_eq_true", filter: "IsArchived eq true");
            yield return A("isarchived_eq_false", filter: "IsArchived eq false");
            yield return A("closedtime_eq_null", filter: "ClosedTime eq null");
            yield return A("closedtime_ne_null", filter: "ClosedTime ne null");

            // ─ logical: and / or / not / parentheses ─
            yield return A("and_two", filter: "IsActive eq true and Quantity gt 5");
            yield return A("and_three", filter: "IsActive eq true and Quantity gt 5 and Price lt 100");
            yield return A("or_two", filter: "Status eq 'Active' or Status eq 'Pending'");
            yield return A("or_three", filter: "Quantity eq 0 or Quantity eq 10 or Quantity eq 999");
            yield return A("not_prefix", filter: "not (IsActive eq true)");
            yield return A("not_contains", filter: "not contains(Name, 'get')");
            yield return A("paren_nested", filter: "(IsActive eq true and Quantity gt 5) or Status eq 'Closed'");
            yield return A("paren_deep", filter: "((Quantity gt 0 and Quantity lt 100) or Price gt 50) and IsActive eq true");
            yield return A("and_or_mix", filter: "Status eq 'Active' and (Quantity gt 5 or Price lt 10)");

            // ─ in / notIn — frontend expands these; include BOTH the raw form (gap) AND expanded form ─
            yield return A("in_raw", filter: "Status in ('Active','Pending')");
            yield return A("in_expanded", filter: "(Status eq 'Active' or Status eq 'Pending')");
            yield return A("notin_raw", filter: "not (Status in ('Closed'))");
            yield return A("notin_expanded", filter: "not (Status eq 'Closed')");

            // ─ string functions ─
            yield return A("fn_contains", filter: "contains(Name, 'idge')");
            yield return A("fn_startswith", filter: "startswith(Code, 'AAA')");
            yield return A("fn_endswith", filter: "endswith(Code, '001')");
            yield return A("fn_tolower", filter: "tolower(Name) eq 'widget'");
            yield return A("fn_toupper", filter: "toupper(Code) eq 'AAA-001'");
            yield return A("fn_trim", filter: "trim(Name) eq 'Widget'");
            yield return A("fn_length", filter: "length(Name) eq 6");
            yield return A("fn_indexof", filter: "indexof(Name, 'i') eq 1");
            yield return A("fn_substring2", filter: "substring(Code, 4) eq '001'");
            yield return A("fn_substring3", filter: "substring(Code, 0, 3) eq 'AAA'");
            yield return A("fn_concat", filter: "concat(Name, Code) eq 'WidgetAAA-001'");
            yield return A("fn_concat_literal", filter: "concat(Code, '-X') eq 'AAA-001-X'");
            // replace — spec lists it, engine does NOT implement it
            yield return A("fn_replace", filter: "replace(Code, '-', '_') eq 'AAA_001'");

            // ─ date functions ─
            yield return A("fn_year", filter: "year(CreatedTime) eq 2024");
            yield return A("fn_month", filter: "month(CreatedTime) eq 1");
            yield return A("fn_day", filter: "day(CreatedTime) eq 15");
            yield return A("fn_hour", filter: "hour(CreatedTime) eq 9");
            yield return A("fn_minute", filter: "minute(CreatedTime) eq 30");
            yield return A("fn_second", filter: "second(CreatedTime) eq 59");

            // ─ math functions ─
            yield return A("fn_round", filter: "round(Price) eq 20");
            yield return A("fn_floor", filter: "floor(Price) eq 19");
            yield return A("fn_ceiling", filter: "ceiling(Price) eq 20");
            // arithmetic operators — spec lists add/sub/mul/div/mod; engine has none
            yield return A("arith_add", filter: "Quantity add 5 eq 15");
            yield return A("arith_mod", filter: "Quantity mod 2 eq 0");

            // ─ nested ref-nav filter (depth 1) ─
            yield return A("nested_ref_name", filter: "Category/Name eq 'Beta'");
            yield return A("nested_ref_kind_enum", filter: "Category/Kind eq 'High'");
            yield return A("nested_ref_apostrophe", filter: "Category/Name eq 'O''Hara'");
            // self-ref nav filter
            yield return A("self_ref_parent_name", filter: "Parent/Name eq 'Widget'");
            yield return A("self_ref_parent_null", filter: "ParentId eq null");

            // ─ collection lambdas: any / all / nested $count ─
            yield return A("any_no_predicate", filter: "Tags/any()");
            yield return A("any_predicate", filter: "Tags/any(t: t/Value gt 5)");
            yield return A("any_label", filter: "Tags/any(t: t/Label eq 'red')");
            yield return A("all_predicate", filter: "Tags/all(t: t/Value gt 0)");
            yield return A("all_label", filter: "Tags/all(t: t/Label eq 'red')");
            // Tags/$count gt 0 — collection $count terminal in $filter (handled in MemberPathResolver.WalkPath)
            yield return A("tags_count_gt0", filter: "Tags/$count gt 0");

            // ─ literals: enum / null / escaped string / ISO date / boolean ─
            yield return A("lit_enum", filter: "Status eq 'Closed'");
            yield return A("lit_null", filter: "ClosedTime eq null");
            yield return A("lit_escaped_string", filter: "Name eq 'O''Brien''s Tool'");
            yield return A("lit_iso_date", filter: "CreatedTime eq 2023-12-31T23:59:59Z");
            yield return A("lit_iso_date_offset", filter: "CreatedTime ge 2024-01-15T09:30:00+00:00");
            yield return A("lit_bool", filter: "IsActive eq true");
            yield return A("lit_decimal", filter: "Price eq 0.99");
            yield return A("lit_negative", filter: "Quantity ge 0 and Quantity le 999");

            // ─ orderby variants ─
            yield return A("orderby_asc", orderBy: "Name asc");
            yield return A("orderby_desc", orderBy: "Name desc");
            yield return A("orderby_default_dir", orderBy: "Price"); // no dir -> asc
            yield return A("orderby_multi", orderBy: "Status asc, Price desc, Id asc");
            yield return A("orderby_nested_ref", orderBy: "Category/Name asc");
            yield return A("orderby_enum", orderBy: "Status asc");

            // ─ paging ─
            yield return A("top_0", top: 0);
            yield return A("top_1", top: 1, orderBy: "Id asc");
            yield return A("top_3", top: 3, orderBy: "Id asc");
            yield return A("skip_1", skip: 1, orderBy: "Id asc");
            yield return A("skip_4", skip: 4, orderBy: "Id asc");
            yield return A("top_skip_combo", top: 2, skip: 2, orderBy: "Id asc");
            yield return A("count_only", count: true);
            yield return A("count_with_filter", filter: "IsActive eq true", count: true);
            yield return A("count_with_paging", filter: "IsActive eq true", top: 1, count: true);

            // ─ $select flat / nested ─
            yield return A("select_single", select: "Id");
            yield return A("select_multi", select: "Id,Name,Quantity,Price");
            yield return A("select_with_enum", select: "Id,Status,Priority");
            yield return A("select_nested_path", select: "Id,Category/Name");
            yield return A("select_all_scalars", select: "Id,Name,Code,Quantity,Price,IsActive,IsArchived,CreatedTime,ClosedTime,Status,Priority");

            // ─ $expand: ref / collection / nested / with $select ─
            yield return A("expand_ref", expand: "Category");
            yield return A("expand_ref_select", expand: "Category($select=Name)");
            yield return A("expand_collection", expand: "Tags");
            yield return A("expand_collection_select", expand: "Tags($select=Label)");
            yield return A("expand_self_ref", expand: "Parent");
            yield return A("expand_self_ref_select", expand: "Parent($select=Name)");
            yield return A("expand_multi", expand: "Category,Tags");
            // deeper nested $expand (depth 2): Category then back to its Items
            yield return A("expand_nested_deep", expand: "Category($expand=Items($select=Id))");
            yield return A("expand_self_ref_chain", expand: "Parent($expand=Parent($select=Name))");

            // ─ $select + $expand combined ─
            yield return A("select_and_expand", select: "Id,Name", expand: "Category($select=Name)");
            yield return A("select_expand_collection", select: "Id", expand: "Tags($select=Value)");

            // ─ representative combinations (NOT cross-product) ─
            yield return A("combo_filter_orderby_top", filter: "IsActive eq true", orderBy: "Price desc", top: 2);
            yield return A("combo_filter_orderby_skip_top", filter: "Quantity gt 0", orderBy: "Quantity asc", skip: 1, top: 2);
            yield return A("combo_filter_count_select", filter: "Status eq 'Active'", select: "Id,Name", count: true);
            yield return A("combo_filter_expand", filter: "Category/Name eq 'Alpha'", expand: "Category($select=Name)");
            yield return A("combo_nested_filter_orderby", filter: "Category/Kind eq 'Low'", orderBy: "Price desc");
            yield return A("combo_lambda_filter_top", filter: "Tags/any(t: t/Value gt 0)", orderBy: "Id asc", top: 3);
            yield return A("combo_string_fn_and", filter: "startswith(Code, 'AAA') and IsActive eq true");
            yield return A("combo_date_fn_or", filter: "year(CreatedTime) eq 2024 or year(CreatedTime) eq 2025");
            yield return A("combo_full", filter: "IsActive eq true and Quantity ge 10", orderBy: "Price desc, Id asc", skip: 0, top: 5, count: true, select: "Id,Name,Price");

            // ─ extended single-feature coverage: string fns over additional fields ─
            yield return A("contains_code", filter: "contains(Code, 'AAA')");
            yield return A("startswith_name", filter: "startswith(Name, 'Wid')");
            yield return A("endswith_name", filter: "endswith(Name, 'get')");
            yield return A("tolower_code", filter: "tolower(Code) eq 'aaa-001'");
            yield return A("toupper_name", filter: "toupper(Name) eq 'WIDGET'");
            yield return A("length_code", filter: "length(Code) eq 7");
            yield return A("indexof_code", filter: "indexof(Code, '-') eq 3");
            yield return A("substring_name2", filter: "substring(Name, 1) eq 'idget'");
            yield return A("concat_three_args_emulated", filter: "concat(concat(Name, '/'), Code) eq 'Widget/AAA-001'");
            yield return A("contains_apostrophe", filter: "contains(Name, 'Brien')");

            // ─ extended comparison coverage on nullable / enum fields ─
            yield return A("priority_eq_low", filter: "Priority eq 'Low'");
            yield return A("priority_eq_medium", filter: "Priority eq 'Medium'");
            yield return A("isarchived_null_or_true", filter: "IsArchived eq null or IsArchived eq true");
            yield return A("closedtime_ge_date", filter: "ClosedTime ge 2024-07-01T00:00:00Z");
            yield return A("price_between", filter: "Price ge 5 and Price le 50");
            yield return A("quantity_in_expanded", filter: "Quantity eq 0 or Quantity eq 50 or Quantity eq 999");

            // ─ extended date-fn coverage ─
            yield return A("year_2023", filter: "year(CreatedTime) eq 2023");
            yield return A("year_2025", filter: "year(CreatedTime) eq 2025");
            yield return A("month_specific", filter: "month(CreatedTime) eq 5");
            yield return A("day_specific", filter: "day(CreatedTime) eq 28");

            // ─ extended orderby + paging permutations ─
            yield return A("orderby_price_then_name", orderBy: "Price asc, Name asc");
            yield return A("orderby_desc_then_top", orderBy: "Quantity desc", top: 2);
            yield return A("skip_past_end", skip: 10);
            yield return A("top_large", top: 100, orderBy: "Id asc");

            // ─ extended select/expand permutations ─
            yield return A("select_two_navs_paths", select: "Id,Category/Name,Category/Kind");
            yield return A("expand_collection_with_filtered_shape", expand: "Tags($select=Label,Value)");
            yield return A("expand_ref_and_collection_select", expand: "Category($select=Name),Tags($select=Label)");
            yield return A("select_then_orderby", select: "Id,Name", orderBy: "Name desc");

            // ─ extended combinations (representative, not cross-product) ─
            yield return A("combo_lambda_all_and_filter", filter: "Tags/all(t: t/Value gt 0) and IsActive eq true");
            yield return A("combo_nested_ref_and_top", filter: "Category/Name ne 'Alpha'", orderBy: "Id asc", top: 3);
            yield return A("combo_not_and_orderby", filter: "not (Status eq 'Closed')", orderBy: "CreatedTime asc");
            yield return A("combo_date_range_count", filter: "CreatedTime ge 2024-01-01T00:00:00Z and CreatedTime lt 2025-01-01T00:00:00Z", count: true);

            // ─ [JsonIgnore] tightening — THE single intentional divergence ─
            yield return A("jsonignore_select_secret", select: "Id,Name,Secret", expect: ExpectKind.Reject400,
                note: "Engine REJECTS $select naming a [JsonIgnore] property ('Secret') — hidden-property guard throws OdataQueryException -> 400. User-confirmed desired behavior (2026-06-23); legacy instead surfaced the property.");

            // ─ $apply — every form expected to 400 ─
            yield return A("apply_aggregate", apply: "aggregate(Price with sum as Total)", expect: ExpectKind.Reject400,
                note: "$apply is unsupported; engine throws UnsupportedQueryOptionException -> HTTP 400.");
            yield return A("apply_groupby", apply: "groupby((Status))", expect: ExpectKind.Reject400);
            yield return A("apply_filter", apply: "filter(IsActive eq true)", expect: ExpectKind.Reject400);
        }
    }
}

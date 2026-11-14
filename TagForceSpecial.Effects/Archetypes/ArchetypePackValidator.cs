namespace TagForceSpecial.Effects.Archetypes;

/// <summary>Validates authoring data against explicit card and dependency exports without registering gameplay.</summary>
public static class ArchetypePackValidator
{
    public static IReadOnlyList<ArchetypeValidationIssue> Validate(ArchetypePackDocument pack,
        ArchetypeValidationContext context, ArchetypeValidationMode mode = ArchetypeValidationMode.ReviewedContent)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(context);
        var issues = new List<ArchetypeValidationIssue>();
        void Error(string path, string code, string message) => issues.Add(new(path, code, message));
        void Text(string? text, string path)
        {
            if (string.IsNullOrWhiteSpace(text) || text != text.Trim()) Error(path, "invalid-text", "Nonempty text without surrounding whitespace is required.");
        }
        void Id(string? id, string path)
        {
            if (!ContentIdentifier.IsValid(id)) Error(path, "invalid-id", "A valid namespaced identifier is required.");
        }
        void Schema(int schema, string path)
        {
            if (schema != ArchetypePackJson.SchemaVersion) Error(path, "schema-version", "Unsupported schema version.");
        }
        void Evidence(ArchetypeEvidence? evidence, string path)
        {
            if (evidence is null) { Error(path, "missing-evidence", "Provenance is required."); return; }
            Text(evidence.Reference, path + ".reference"); Text(evidence.Reason, path + ".reason");
            if (evidence.Sha256 is not { Length: 64 } || !evidence.Sha256.All(Uri.IsHexDigit))
                Error(path + ".sha256", "invalid-hash", "A SHA-256 hexadecimal digest is required.");
        }
        void Review(ArchetypeReviewStatus review, string path)
        {
            if (!Enum.IsDefined(review)) Error(path, "invalid-review", "Unknown review status.");
            else if (mode == ArchetypeValidationMode.ReviewedContent && review != ArchetypeReviewStatus.Reviewed)
                Error(path, "unreviewed", "Candidate claims are allowed only in diagnostic validation.");
        }
        IReadOnlyList<T> Items<T>(IReadOnlyList<T>? items, string path)
        {
            if (items is not null) return items;
            Error(path, "missing-list", "An explicit list is required."); return Array.Empty<T>();
        }
        void Version(string? version, string path)
        {
            if (string.IsNullOrWhiteSpace(version) || version.Length > 80 || version.Any(char.IsWhiteSpace))
                Error(path, "invalid-version", "An exact revision token of at most 80 characters is required.");
        }
        if (!Enum.IsDefined(mode)) Error("mode", "invalid-mode", "Unknown validation mode.");
        if (pack.Manifest is null || pack.Definitions is null || pack.Memberships is null ||
            pack.IdentityRules is null || pack.NativeMappings is null)
        {
            Error("pack", "missing-document", "All five authoring documents are required.");
            return issues.AsReadOnly();
        }
        var manifest = pack.Manifest;
        Schema(manifest.SchemaVersion, "manifest.schemaVersion");
        Id(manifest.PackId.Value, "manifest.packId"); Version(manifest.Version, "manifest.version");
        Evidence(manifest.Evidence, "manifest.evidence");
        if (!Enum.IsDefined(manifest.Kind)) Error("manifest.kind", "invalid-kind", "Unknown pack kind.");
        var profiles = Items(manifest.Profiles, "manifest.profiles");
        if (profiles.Count == 0 || profiles.Distinct().Count() != profiles.Count || profiles.Any(p => !Enum.IsDefined(p)))
            Error("manifest.profiles", "invalid-profiles", "At least one distinct known profile is required.");
        if (manifest.Kind == ArchetypePackKind.Expansion && profiles.Contains(ArchetypeContentProfile.TfsClassic))
            Error("manifest.profiles", "classic-expansion", "Expansion packs cannot target the historical Classic profile.");

        var cards = new HashSet<SourceCardId>();
        foreach (var card in Items(context.Cards, "context.cards"))
        {
            Id(card.Value, "context.cards");
            if (!cards.Add(card)) Error("context.cards", "duplicate-card", "Card identifiers must be unique.");
        }
        var exports = new Dictionary<ArchetypePackId, ArchetypeDependencyExports>();
        foreach (var dependency in Items(context.Dependencies, "context.dependencies"))
        {
            if (dependency is null) { Error("context.dependencies", "null-entry", "A dependency export cannot be null."); continue; }
            Id(dependency.PackId.Value, "context.dependencies.packId"); Version(dependency.Version, "context.dependencies.version");
            if (!exports.TryAdd(dependency.PackId, dependency)) Error("context.dependencies", "duplicate-dependency", "Dependency exports must have unique pack IDs.");
        }
        var known = new HashSet<ArchetypeId>();
        var dependencies = new HashSet<ArchetypePackId>();
        foreach (var dependency in Items(manifest.Dependencies, "manifest.dependencies"))
        {
            if (dependency is null) { Error("manifest.dependencies", "null-entry", "A dependency cannot be null."); continue; }
            Id(dependency.PackId.Value, "manifest.dependencies.packId"); Version(dependency.Version, "manifest.dependencies.version");
            if (!dependencies.Add(dependency.PackId)) Error("manifest.dependencies", "duplicate-dependency", "Duplicate dependency declaration.");
            if (dependency.PackId == manifest.PackId) Error("manifest.dependencies", "self-dependency", "A pack cannot depend on itself.");
            if (!exports.TryGetValue(dependency.PackId, out var available) || available.Version != dependency.Version)
                Error("manifest.dependencies", "unresolved-dependency", "A dependency with this exact version must be supplied.");
            else foreach (var id in Items(available.Archetypes, "context.dependencies.archetypes"))
            {
                Id(id.Value, "context.dependencies.archetypes");
                if (!known.Add(id)) Error("context.dependencies.archetypes", "duplicate-archetype", "Dependencies export a conflicting archetype ID.");
            }
        }

        Schema(pack.Definitions.SchemaVersion, "definitions.schemaVersion");
        var names = new Dictionary<string, ArchetypeId>(StringComparer.OrdinalIgnoreCase);
        var externalIds = new HashSet<(string System, string Value)>();
        void Name(string? name, ArchetypeId id, string path)
        {
            Text(name, path);
            if (name is not null && !names.TryAdd(name, id)) Error(path, "duplicate-name", "Display names and aliases must be unambiguous within a pack.");
        }
        foreach (var definition in Items(pack.Definitions.Definitions, "definitions.definitions"))
        {
            if (definition is null) { Error("definitions.definitions", "null-entry", "A definition cannot be null."); continue; }
            string path = $"definitions.{definition.Id}";
            Id(definition.Id.Value, path + ".id");
            if (!known.Add(definition.Id)) Error(path, "duplicate-archetype", "An archetype ID cannot be defined twice or replace a dependency definition.");
            Review(definition.Review, path + ".review"); Evidence(definition.Evidence, path + ".evidence");
            Name(definition.Name, definition.Id, path + ".name");
            foreach (var alias in Items(definition.Aliases, path + ".aliases")) Name(alias, definition.Id, path + ".aliases");
            foreach (var external in Items(definition.ExternalIds, path + ".externalIds"))
            {
                if (external is null) { Error(path + ".externalIds", "null-entry", "An external identifier cannot be null."); continue; }
                Text(external.System, path + ".externalIds.system"); Text(external.Value, path + ".externalIds.value");
                Evidence(external.Evidence, path + ".externalIds.evidence");
                if (!externalIds.Add((external.System, external.Value))) Error(path + ".externalIds", "duplicate-external-id", "An external identifier cannot identify multiple definitions.");
            }
        }
        void Archetype(ArchetypeId id, string path)
        {
            Id(id.Value, path);
            if (!known.Contains(id)) Error(path, "unresolved-archetype", "Archetype is neither locally defined nor exported by a declared dependency.");
        }
        void Card(SourceCardId id, string path)
        {
            Id(id.Value, path);
            if (!cards.Contains(id)) Error(path, "unresolved-card", "An explicit source card identity is required in the validation context.");
        }
        var edges = new HashSet<(ArchetypeId Child, ArchetypeId Parent)>();
        foreach (var relationship in Items(pack.Definitions.Relationships, "definitions.relationships"))
        {
            if (relationship is null) { Error("definitions.relationships", "null-entry", "A relationship cannot be null."); continue; }
            const string path = "definitions.relationships";
            Archetype(relationship.Child, path + ".child"); Archetype(relationship.Parent, path + ".parent");
            Review(relationship.Review, path + ".review"); Evidence(relationship.Evidence, path + ".evidence");
            if (relationship.Child == relationship.Parent) Error(path, "self-relationship", "An archetype cannot be its own subset.");
            if (!edges.Add((relationship.Child, relationship.Parent))) Error(path, "duplicate-relationship", "Duplicate subset relationship.");
        }
        var parents = edges.GroupBy(e => e.Child).ToDictionary(g => g.Key, g => g.Select(e => e.Parent).ToArray());
        var incoming = known.ToDictionary(id => id, _ => 0);
        foreach (var edge in edges) if (incoming.ContainsKey(edge.Parent) && incoming.ContainsKey(edge.Child)) incoming[edge.Parent]++;
        var ready = new Queue<ArchetypeId>(incoming.Where(p => p.Value == 0).Select(p => p.Key));
        int visited = 0;
        while (ready.TryDequeue(out var id))
        {
            visited++;
            if (parents.TryGetValue(id, out var targets))
                foreach (var target in targets) if (incoming.ContainsKey(target) && --incoming[target] == 0) ready.Enqueue(target);
        }
        if (visited != known.Count) Error("definitions.relationships", "relationship-cycle", "Subset relationships must be acyclic.");

        Schema(pack.Memberships.SchemaVersion, "memberships.schemaVersion");
        var memberCards = new HashSet<SourceCardId>();
        foreach (var membership in Items(pack.Memberships.Memberships, "memberships.memberships"))
        {
            if (membership is null) { Error("memberships.memberships", "null-entry", "A membership cannot be null."); continue; }
            string path = $"memberships.{membership.CardId}";
            Card(membership.CardId, path + ".cardId"); Review(membership.Review, path + ".review"); Evidence(membership.Evidence, path + ".evidence");
            if (!memberCards.Add(membership.CardId)) Error(path, "duplicate-membership", "Combine a card's memberships in one declaration per pack.");
            var archetypes = Items(membership.Archetypes, path + ".archetypes");
            if (archetypes.Count == 0 || archetypes.Distinct().Count() != archetypes.Count)
                Error(path, "invalid-memberships", "A nonempty list of distinct archetype IDs is required.");
            foreach (var id in archetypes) Archetype(id, path + ".archetypes");
        }
        Schema(pack.IdentityRules.SchemaVersion, "identityRules.schemaVersion");
        var ruleIds = new HashSet<string>(StringComparer.Ordinal);
        var permanentNames = new Dictionary<SourceCardId, string>();
        var permanentNameGroups = new Dictionary<SourceCardId, IReadOnlyList<ArchetypeId>?>();
        foreach (var rule in Items(pack.IdentityRules.Rules, "identityRules.rules"))
        {
            if (rule is null) { Error("identityRules.rules", "null-entry", "An identity rule cannot be null."); continue; }
            string path = $"identityRules.{rule.Id}";
            Id(rule.Id, path + ".id"); Card(rule.CardId, path + ".cardId");
            Review(rule.Review, path + ".review"); Evidence(rule.Evidence, path + ".evidence");
            if (rule.Id is not null && !ruleIds.Add(rule.Id)) Error(path, "duplicate-rule", "Identity rule IDs must be unique.");
            bool conditional = rule.Treatment is ArchetypeIdentityTreatment.ConditionalName or ArchetypeIdentityTreatment.ConditionalArchetype;
            if (conditional != (rule.Condition is not null))
                Error(path, "invalid-condition", "Only conditional treatment requires a condition.");
            if (rule.Condition is { } condition)
            {
                var locations = Items(condition.Locations, path + ".condition.locations");
                if (locations.Count == 0 || locations.Distinct().Count() != locations.Count || locations.Any(l => !Enum.IsDefined(l)) ||
                    condition.Controller is { } controller && !Enum.IsDefined(controller))
                    Error(path, "invalid-condition", "Conditions require distinct known locations and a valid optional controller.");
            }
            if (rule.NameArchetypes is { } nameGroups)
            {
                if (nameGroups.Distinct().Count() != nameGroups.Count) Error(path, "duplicate-name-archetype", "Name archetypes must be distinct.");
                foreach (var group in nameGroups) Archetype(group, path + ".nameArchetypes");
            }
            if (rule.Treatment is ArchetypeIdentityTreatment.PermanentName or ArchetypeIdentityTreatment.ConditionalName && rule.Archetype is null)
            {
                Text(rule.Name, path + ".name");
                if (conditional && rule.NameArchetypes is null) Error(path, "missing-name-archetypes", "Conditional names require explicit name membership, including an empty list.");
                if (!conditional && rule.Name is not null)
                {
                    if (permanentNames.TryGetValue(rule.CardId, out var previous) && previous != rule.Name)
                        Error(path, "conflicting-name-treatment", "A card cannot have contradictory permanent names in one pack.");
                    else permanentNames[rule.CardId] = rule.Name;
                    if (permanentNameGroups.TryGetValue(rule.CardId, out var previousGroups) &&
                        ((previousGroups is null) != (rule.NameArchetypes is null) || !(previousGroups ?? []).ToHashSet().SetEquals(rule.NameArchetypes ?? [])))
                        Error(path, "conflicting-name-treatment", "Permanent name memberships cannot contradict each other.");
                    else permanentNameGroups[rule.CardId] = rule.NameArchetypes;
                }
            }
            else if (rule.Treatment is ArchetypeIdentityTreatment.PermanentArchetype or ArchetypeIdentityTreatment.ConditionalArchetype && rule.Name is null && rule.NameArchetypes is null && rule.Archetype is { } target)
                Archetype(target, path + ".archetype");
            else Error(path, "invalid-treatment", "Name treatment needs a name; archetype treatment needs only an archetype.");
        }
        Schema(pack.NativeMappings.SchemaVersion, "nativeMappings.schemaVersion");
        var nativeIds = new HashSet<int>();
        var mappings = Items(pack.NativeMappings.Mappings, "nativeMappings.mappings");
        if (manifest.Kind == ArchetypePackKind.Expansion && mappings.Count != 0)
            Error("nativeMappings", "expansion-native-mapping", "An expansion cannot redefine native-group mappings.");
        foreach (var mapping in mappings)
        {
            if (mapping is null) { Error("nativeMappings.mappings", "null-entry", "A native mapping cannot be null."); continue; }
            string path = $"nativeMappings.{mapping.NativeGroupId}";
            if (mapping.NativeGroupId < 0) Error(path, "invalid-native-group", "A native group ID cannot be negative.");
            if (!nativeIds.Add(mapping.NativeGroupId)) Error(path, "duplicate-native-group", "Native group IDs must be unique within a pack.");
            if (mapping.Archetype is { } id) Archetype(id, path + ".archetype");
            Review(mapping.Review, path + ".review"); Evidence(mapping.Evidence, path + ".evidence");
        }
        return Array.AsReadOnly(issues.OrderBy(i => i.Path, StringComparer.Ordinal).ThenBy(i => i.Code, StringComparer.Ordinal).ToArray());
    }
}

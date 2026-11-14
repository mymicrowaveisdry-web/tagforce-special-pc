namespace TagForceSpecial.Effects.Archetypes;

internal static class ArchetypePackageValidation
{
    internal static bool IsHash(string? hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);
    internal static bool IsVersion(string? version) => !string.IsNullOrWhiteSpace(version) && version.Length <= 80 && !version.Any(char.IsWhiteSpace);
    internal static void Evidence(ArchetypeReviewStatus review, ArchetypeEvidence? evidence)
    {
        if (review != ArchetypeReviewStatus.Reviewed || evidence is null || !IsHash(evidence.Sha256) ||
            string.IsNullOrWhiteSpace(evidence.Reference) || string.IsNullOrWhiteSpace(evidence.Reason))
            throw new InvalidDataException("Package facts and mappings require reviewed provenance and a SHA-256.");
    }

    internal static void Validate(ArchetypePackageDocument package, ArchetypeValidationContext context)
    {
        if (context.Cards is null || package.Pack is null || package.Catalog is null || package.CardMappings is null || package.MappingSources is null ||
            package.Catalog.SchemaVersion != 1 || package.CardMappings.SchemaVersion != 1 || package.MappingSources.SchemaVersion != 1 ||
            package.Catalog.Cards is null || package.Catalog.Cards.Any(c => c is null || c.Facts is null) ||
            package.CardMappings.Mappings is null || package.MappingSources.Sources is null)
            throw new InvalidDataException("All package documents, schemas and collections are required.");
        ArchetypeCardFacts.ValidateAll(package.Catalog.Cards.Select(c => c.Facts).ToArray());
        var knownCards = context.Cards.ToHashSet();
        var localIds = new HashSet<uint>();
        foreach (var card in package.Catalog.Cards)
        {
            Evidence(card.Review, card.Evidence);
            if (card.CatalogCardId == 0 || !localIds.Add(card.CatalogCardId) || !knownCards.Contains(card.Facts.CardId))
                throw new InvalidDataException("Local catalog IDs must be nonzero, unique and mapped to known source identities.");
        }
        var issues = ArchetypePackValidator.Validate(package.Pack, context);
        if (issues.Count != 0) throw new InvalidDataException(string.Join("; ", issues.Select(i => $"{i.Path}: {i.Code}: {i.Message}")));
        var sources = new Dictionary<string, ArchetypeMappingSource>(StringComparer.Ordinal);
        foreach (var source in package.MappingSources.Sources)
        {
            if (source is null || !ContentIdentifier.IsValid(source.System + ":source") || !IsVersion(source.Version))
                throw new InvalidDataException("Mapping sources require an explicit system and dataset version.");
            Evidence(source.Review, source.Evidence);
            if (!sources.TryAdd(source.System, source)) throw new InvalidDataException("Mapping source systems must be unique within a package.");
        }
        void Source(string system, ArchetypeReviewStatus review, ArchetypeEvidence evidence)
        {
            Evidence(review, evidence);
            if (!sources.TryGetValue(system, out var source) || !source.Evidence.Sha256.Equals(evidence.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("External mappings must match an explicitly versioned dataset hash.");
        }
        var externalCards = new HashSet<SourceCardId>();
        foreach (var mapping in package.CardMappings.Mappings)
        {
            if (mapping is null || !ContentIdentifier.IsValid(mapping.ExternalId.Value) || !ContentIdentifier.IsValid(mapping.CardId.Value) ||
                !externalCards.Add(mapping.ExternalId) || !knownCards.Contains(mapping.CardId))
                throw new InvalidDataException("External card mappings require unique namespaced aliases and a known canonical card.");
            Source(mapping.ExternalId.Value.Split(':')[0], mapping.Review, mapping.Evidence);
        }
        foreach (var external in package.Pack.Definitions.Definitions.SelectMany(d => d.ExternalIds))
            Source(external.System, ArchetypeReviewStatus.Reviewed, external.Evidence);
        if (package.Pack.Manifest.Kind == ArchetypePackKind.Native)
            ValidateNative(package);
        else if (package.NativeTable is not null)
            throw new InvalidDataException("Only historical native packs can contain a native table.");
    }

    private static void ValidateNative(ArchetypePackageDocument package)
    {
        if (package.NativeTable is null) throw new InvalidDataException("A native package must preserve its historical table.");
        var index = new NativeArchetypeIndex(package.NativeTable);
        if (!index.Document.SourceSha256.Equals(package.Pack.Manifest.Evidence.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Native table and pack source identities differ.");
        var cards = package.Catalog.Cards.ToDictionary(c => c.CatalogCardId);
        if (!cards.Values.Where(c => c.Facts.IsToken).Select(c => c.CatalogCardId).Order().SequenceEqual(index.Document.TokenCardIds) ||
            cards.Values.Any(c => !c.Evidence.Sha256.Equals(index.Document.CatalogSha256, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Native catalog provenance or Token metadata differs from the historical source.");
        var mappings = package.Pack.NativeMappings.Mappings.OrderBy(m => m.NativeGroupId).ToArray();
        if (mappings.Length != index.GroupCount || mappings.Where((m, i) => m.NativeGroupId != i).Any())
            throw new InvalidDataException("Every historical slot must be preserved in native mappings.");
        var expected = new HashSet<(SourceCardId Card, ArchetypeId Archetype)>();
        for (int slot = 0; slot < index.GroupCount; slot++)
            foreach (uint id in index.GetMembers(slot))
            {
                if (!cards.TryGetValue(id, out var card)) throw new InvalidDataException("Native table refers to an absent catalog identity.");
                if (!index.IsToken(id) && mappings[slot].Archetype is { } archetype) expected.Add((card.Facts.CardId, archetype));
            }
        var actual = package.Pack.Memberships.Memberships.SelectMany(m => m.Archetypes.Select(a => (m.CardId, a))).ToHashSet();
        if (!expected.SetEquals(actual)) throw new InvalidDataException("Native semantic memberships differ from the preserved numeric table.");
    }
}

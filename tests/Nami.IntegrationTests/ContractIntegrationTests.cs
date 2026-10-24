using System.Text.Json;
using Nami.Api.Contracts;
using Nami.Tests.Infrastructure;
namespace Nami.IntegrationTests;

[TestClass]
[TestCategory("Integration")]
[DependsOn(typeof(IngestStoreTests), nameof(IngestStoreTests.Catalog_EmbeddedConstants_MatchRegistrySnapshot))]
public sealed class ContractIntegrationTests(TestContext context)
{
    [TestMethod]
    public void SerializerContext_CoversEveryContractType()
    {
        var schemaPath=Path.Combine(ProcessRunner.RepositoryRoot,"internal/nami.contracts/Nami/Api/Contracts/contract-schemas.json");
        using var schemas=JsonDocument.Parse(File.ReadAllText(schemaPath));
        var checkedTypes=0;
        foreach(var schema in schemas.RootElement.EnumerateObject())
        {
            // Pure scalars have no generated CLR wrapper. Every model/union/enum has one.
            var clr=typeof(ContractJson).Assembly.GetType("Nami.Api.Contracts."+schema.Name);
            if(clr is null && !schema.Value.TryGetProperty("enum",out _) && schema.Value.TryGetProperty("type",out var kind) && kind.ValueKind==JsonValueKind.String && kind.GetString() is "string" or "integer" or "number" or "boolean")continue;
            if(clr is null && schema.Name=="Common.AttributeDouble")continue;
            if(clr is null)throw new AssertFailedException("Missing generated CLR contract: "+schema.Name);
            Assert.AreEqual(schema.Name,ContractTypes.NameOf(clr));
            Assert.IsNotNull(ContractTypes.TypeInfo(schema.Name),schema.Name);
            checkedTypes++;
        }
        Assert.IsGreaterThan(40,checkedTypes);
    }

    [TestMethod, Timeout(30000, CooperativeCancellation = true)]
    public async Task ContractRevision_DocChange_Unchanged()
    {
        const string script = """
        import { canonicalize } from './contracts/scripts/lib/canonical-schema.mjs';
        import { readFileSync } from 'node:fs';
        import { createHash } from 'node:crypto';
        const schemas=JSON.parse(readFileSync('internal/nami.contracts/Nami/Api/Contracts/contract-schemas.json','utf8'));
        const hash=x=>'sha256:'+createHash('sha256').update(JSON.stringify(canonicalize(x))).digest('hex');
        const original=hash(schemas);
        schemas['Common.ToolError'].description='A different description';
        const presentation=hash(schemas);
        schemas['Common.ToolError'].properties.code.minLength=2;
        console.log(JSON.stringify([original,presentation,hash(schemas)]));
        """;
        var result=await ProcessRunner.RunAsync("node",["--input-type=module","-e",script],ProcessRunner.RepositoryRoot,context.CancellationToken);
        Assert.AreEqual(0,result.ExitCode,result.Error);
        using var hashes=JsonDocument.Parse(result.Output);
        Assert.AreEqual(ContractRevision.Value,hashes.RootElement[0].GetString());
        Assert.AreEqual(hashes.RootElement[0].GetString(),hashes.RootElement[1].GetString());
        Assert.AreNotEqual(hashes.RootElement[0].GetString(),hashes.RootElement[2].GetString());
        Assert.AreEqual(71,ContractRevision.Value.Length);
    }

}

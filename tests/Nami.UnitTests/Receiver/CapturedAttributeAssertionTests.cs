using Nami.Api.Contracts.Common;
using ContractAttribute = Nami.Api.Contracts.Common.Attribute;

namespace Nami.UnitTests.Receiver;

[TestClass]
[TestCategory("Fast")]
public class CapturedAttributeAssertionTests
{
    public enum InvalidCapturedArray
    {
        Missing,
        DuplicateKey,
        Scalar,
        NonString,
        Reordered
    }

    private const string Key = "http.request.header.x-example";

    [TestMethod]
    public void Should_BePreserved_When_CapturedAttributeAssertion_EmptyValuesDuplicatesAndEmbeddedCommas()
    {
        string[] expected = ["", "first", "first", "a,b"];
        ContractAttribute[] attributes =
        [
            new()
            {
                Key = Key,
                Value = new AttributeValue.ArrayValue([
                    .. expected.Select(value => new AttributeValue.StringValue(value))
                ])
            }
        ];
        Assert.That.HasCapturedValues(attributes, Key, expected);
    }

    [TestMethod]
    [DataRow(InvalidCapturedArray.Missing)]
    [DataRow(InvalidCapturedArray.DuplicateKey)]
    [DataRow(InvalidCapturedArray.Scalar)]
    [DataRow(InvalidCapturedArray.NonString)]
    [DataRow(InvalidCapturedArray.Reordered)]
    public void Should_BeRejected_When_CapturedAttributeAssertion_MissingAmbiguousOrChangedEvidence(
        InvalidCapturedArray violation)
    {
        var attributes = CapturedAttributes(violation);
        var failure =
            Assert.ThrowsExactly<AssertFailedException>(() =>
                Assert.That.HasCapturedValues(attributes, Key, "first", "second"));
        Assert.Contains(Key, failure.Message);
    }

    private static ContractAttribute[] CapturedAttributes(InvalidCapturedArray violation)
    {
        var valid = new ContractAttribute
        {
            Key = Key,
            Value = new AttributeValue.ArrayValue([
                new AttributeValue.StringValue("first"), new AttributeValue.StringValue("second")
            ])
        };
        return violation switch
        {
            InvalidCapturedArray.Missing => [],
            InvalidCapturedArray.DuplicateKey => [valid, valid],
            InvalidCapturedArray.Scalar =>
                [new ContractAttribute { Key = Key, Value = new AttributeValue.StringValue("first") }],
            InvalidCapturedArray.NonString =>
            [
                new ContractAttribute
                    { Key = Key, Value = new AttributeValue.ArrayValue([new AttributeValue.BoolValue(true)]) }
            ],
            InvalidCapturedArray.Reordered =>
            [
                new ContractAttribute
                {
                    Key = Key,
                    Value = new AttributeValue.ArrayValue([
                        new AttributeValue.StringValue("second"), new AttributeValue.StringValue("first")
                    ])
                }
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(violation))
        };
    }
}

using Microsoft.Extensions.Options;

namespace Nami.Hosting;

[OptionsValidator]
internal sealed partial class CollectorStorageOptionsValidator : IValidateOptions<CollectorStorageOptions>;

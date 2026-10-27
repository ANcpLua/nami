namespace Nami.Models.Configuration.Credentials;

model CredentialDefinition {
  key: string;
  roles: string[];
}
model ProjectKeyDocument is Record<CredentialDefinition[]>;

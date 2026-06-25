// Browser glue for fido2-net-lib ceremonies. The server speaks base64url for all binary fields;
// the WebAuthn API speaks ArrayBuffer. These helpers translate in both directions and shape the
// authenticator response into what AuthenticatorAttestationRawResponse expects.

function base64urlToBuffer(value: string): ArrayBuffer {
  const padded = value.replace(/-/g, "+").replace(/_/g, "/");
  const binary = atob(padded.padEnd(padded.length + ((4 - (padded.length % 4)) % 4), "="));
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return bytes.buffer;
}

function bufferToBase64url(value: ArrayBuffer): string {
  const bytes = new Uint8Array(value);
  let binary = "";
  for (const b of bytes) binary += String.fromCharCode(b);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

/** True if this browser/device can do WebAuthn at all. */
export function isPasskeySupported(): boolean {
  return typeof window !== "undefined" && !!window.PublicKeyCredential && !!navigator.credentials;
}

interface RegistrationOptions {
  challenge: string;
  user: { id: string; name: string; displayName: string };
  excludeCredentials?: { id: string; type: string; transports?: string[] }[];
  [key: string]: unknown;
}

/**
 * Runs the registration ceremony: decodes the server options, prompts the authenticator, and
 * returns the attestation in the server's expected JSON shape.
 */
export async function performRegistration(optionsJson: string): Promise<unknown> {
  const options = JSON.parse(optionsJson) as RegistrationOptions;

  const publicKey: PublicKeyCredentialCreationOptions = {
    ...(options as unknown as PublicKeyCredentialCreationOptions),
    challenge: base64urlToBuffer(options.challenge),
    user: {
      ...options.user,
      id: base64urlToBuffer(options.user.id),
    } as PublicKeyCredentialUserEntity,
    excludeCredentials: options.excludeCredentials?.map((c) => ({
      ...c,
      id: base64urlToBuffer(c.id),
    })) as PublicKeyCredentialDescriptor[] | undefined,
  };

  const credential = (await navigator.credentials.create({ publicKey })) as PublicKeyCredential | null;
  if (!credential) {
    throw new Error("Passkey registration was cancelled.");
  }

  const response = credential.response as AuthenticatorAttestationResponse;
  const transports =
    typeof response.getTransports === "function" ? response.getTransports() : [];

  return {
    id: credential.id,
    rawId: bufferToBase64url(credential.rawId),
    type: credential.type,
    extensions: credential.getClientExtensionResults(),
    response: {
      attestationObject: bufferToBase64url(response.attestationObject),
      clientDataJSON: bufferToBase64url(response.clientDataJSON),
      transports,
    },
  };
}

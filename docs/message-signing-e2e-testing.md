# Message Signing E2E Testing

This document describes internal test infrastructure for end-to-end message-signing verification. The harness scene referenced here is test-only and is not part of the public Shapes sample flow.
Do not include an Approov dev key in a production build. A dev key is only for controlled development and test flows, and shipping one causes attestation to pass.

This repository already has useful message-signing coverage, but it is split across unit tests and the Shapes sample app:

- [Tests/Runtime/ApproovDefaultMessageSigningTests.cs](/Users/adriantukendorf/Developer/Tasks/approov-service-unity/Tests/Runtime/ApproovDefaultMessageSigningTests.cs) checks RFC 9421 header generation, optional header coverage, and `Content-Digest` generation.
- [Samples~/ShapesApp/Scripts/ShapesApp.cs](/Users/adriantukendorf/Developer/Tasks/approov-service-unity/Samples~/ShapesApp/Scripts/ShapesApp.cs) exercises the real request pipeline for both `UnityWebRequest` and `HttpClient`.

What is still missing in this repo is a backend under our control that validates the actual signed request produced by the package. The internal Cloudflare worker at `approov/internal-adrian/internal-verifier-worker` is a good fit for that gap.

## Current Review

The Unity-side signer and the worker are compatible for the default flow used here:

- the package signs `@method` and `@target-uri` by default
- it signs the Approov token header after token injection
- it can optionally sign `Authorization`, `Content-Type`, `Content-Length`, and `Content-Digest`
- install signing uses `ecdsa-p256-sha256`
- account signing uses `hmac-sha256`

The worker supports those same signature algorithms and derived components, including `@target-uri`.

## Main Gaps

1. The package tests still stop at header construction; the new harness scene adds client-side end-to-end coverage, but it is internal test infrastructure and still depends on an external worker deployment.
2. A meaningful worker deployment needs real Approov verification material that is not stored in this repository.
3. The current harness is focused on install-signature verification. Account-signature and `Content-Digest` flows still need additional setup.

## Required Secrets

Install-signature validation still requires Approov token verification first, so the worker needs the real Approov token verification key material for your app:

- `APPROOV_TOKEN_SIGNING_ALGORITHM`
- `APPROOV_TOKEN_PASS_PEM` or symmetric `APPROOV_TOKEN_SECRET_*`
- optionally `APPROOV_TOKEN_FAIL_PEM`

For account-signature validation it also needs:

- `APPROOV_ACCOUNT_MESSAGE_SIGNING_SECRET_RAW` or `APPROOV_ACCOUNT_MESSAGE_SIGNING_SECRET_BASE64/BASE64URL`
- optionally `APPROOV_ACCOUNT_MESSAGE_SIGNING_KEY_ID`

Without those values, a deployed worker can only validate synthetic test tokens, not live traffic from this package.

## Recommended Test Strategy

Use three layers:

1. Keep the existing unit tests as the fast wire-format safety net.
2. Use the internal verifier worker as the end-to-end backend for signed request validation.
3. Drive the real client flow from the dedicated harness scene on device for both transports.

Recommended minimum matrix:

1. `Approov Off + None` -> expect `400` with `tokenResult=MISSING_HEADER`.
2. `Approov On + None` -> expect `400` with `tokenResult=PASS` and `messageSigningResult=MISSING_HEADERS`.
3. `Approov On + Install` -> expect `200` with `messageSigningResult=VALID`.
4. `Approov On + Install`, then tamper with a covered header -> expect `400` invalid signature.
5. `POST` request with a readable body and `Content-Digest` enabled on the worker -> expect digest validation to pass.

## Cloudflare Worker Setup

The referenced worker already has the right verification logic. On this machine:

- `wrangler whoami` succeeds
- the worker dependency install succeeds
- the worker test suite passes with `6` tests

Suggested setup flow:

```bash
git clone https://github.com/approov/internal-adrian.git
cd internal-adrian/internal-verifier-worker
npm install
npm test
```

Set the core worker vars in `wrangler.toml` or via environment:

```toml
APPROOV_TOKEN_HEADER_NAME = "Approov-Token"
APPROOV_TOKEN_SIGNING_ALGORITHM = "ES256"
APPROOV_VERIFICATION_STRATEGY = "token"
APPROOV_MESSAGE_SIGNING_MODE = "install"
APPROOV_MESSAGE_SIGNING_TOLERANCE_SECONDS = "60"
APPROOV_VERIFY_CONTENT_DIGEST = "false"
APPROOV_DEBUG = "true"
```

Then set secrets before deploy:

```bash
npx wrangler secret put APPROOV_TOKEN_PASS_PEM
npx wrangler secret put APPROOV_TOKEN_FAIL_PEM
```

For account mode also set:

```bash
npx wrangler secret put APPROOV_ACCOUNT_MESSAGE_SIGNING_SECRET_BASE64URL
```

Deploy with:

```bash
npx wrangler deploy
```

## How To Use It From This Repo

The fastest practical route is:

1. Deploy the verifier worker.
2. Protect the worker host/path in Approov so the app receives real Approov tokens for that destination.
3. Open [Samples~/ShapesApp/Scenes/MessageSigningHarnessScene.unity](/Users/adriantukendorf/Developer/Tasks/approov-service-unity/Samples~/ShapesApp/Scenes/MessageSigningHarnessScene.unity).
4. Enter the deployed worker URL into the runtime `Worker URL` field.
5. Run the harness on device with `UnityWebRequest` and `HttpClient`.

The harness implementation lives in [Samples~/ShapesApp/Scripts/MessageSigningHarnessApp.cs](/Users/adriantukendorf/Developer/Tasks/approov-service-unity/Samples~/ShapesApp/Scripts/MessageSigningHarnessApp.cs). Keep it positioned as internal verification tooling rather than as package example code.

## Notes On Compatibility

- The package default signature expiry window is `15` seconds in [Runtime/Approov/ApproovDefaultMessageSigning.cs](/Users/adriantukendorf/Developer/Tasks/approov-service-unity/Runtime/Approov/ApproovDefaultMessageSigning.cs). A worker tolerance of `60` seconds is safer for mobile-device clock skew than the checked-in `5` second example.
- The package only adds `Content-Digest` when the request body is readable. For the current sample `GET` flow, digest verification is naturally out of scope unless you add a `POST` path.
- The harness auto-test expectations are currently tailored to install-signature verification against the worker response contract in [Samples~/ShapesApp/Scripts/MessageSigningHarnessApp.cs](/Users/adriantukendorf/Developer/Tasks/approov-service-unity/Samples~/ShapesApp/Scripts/MessageSigningHarnessApp.cs).

## Suggested Follow-Up Changes

If we want this fully runnable from this repository, the next changes should be:

1. add a tampered-header negative test path to the harness
2. add a sample `POST` endpoint flow so `Content-Digest` can be exercised
3. extend the harness to account-signature verification once the worker secret is available

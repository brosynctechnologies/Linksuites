# OPay for platform plan billing: design

Approved by the owner on 14 November 2026. Scope: how a business pays LinkSuite for a paid plan, in all seven apps. Out of scope (owner's decisions): each business's own customer payments (stay on the business's own Paystack key); LearnLink course sales, short-let stays and listing boosts (they use the same platform account and move to the chosen provider in a second step, with OPay refunds).

## Decisions

- OPay sits beside Paystack. A server setting `Billing:PlanProvider` (`opay` or `paystack`, env `BILLING_PROVIDER`) picks the provider for new plan checkouts. Switching needs no code change.
- Each `Payment` records its `Provider`; verification and confirmation always go to the provider that took the payment, so old Paystack payments keep working after a switch.
- Course sales, stays and boosts keep the existing single `IPaymentGateway` (the platform Paystack account) unchanged.

## OPay facts used (official docs, doc.opaycheckout.com, checked 14 Nov 2026)

- Create: `POST {base}/api/v1/international/cashier/create`, headers `Authorization: Bearer {PublicKey}`, `MerchantId`. Body: `country` (`NG`), `reference`, `amount { total (kobo), currency (NGN) }`, `returnUrl`, `callbackUrl`, `cancelUrl`, `expireAt` (minutes), `userInfo`, `product` / `productList`. Response `data.cashierUrl`, `data.orderNo`, `data.status`. Success code `00000`.
- Status: `POST {base}/api/v1/international/cashier/status`, headers `Authorization: Bearer {HMAC-SHA512(body, SecretKey) hex}`, `MerchantId`. Body `{ country, reference }`: OPay checks the signature against the fields in alphabetical order, so they are sent in that order (confirmed on the sandbox 14 Nov 2026; `reference` first is refused with `02000`). Response `data.status` in `INITIAL | PENDING | SUCCESS | FAIL | CLOSE`, `data.amount.total`, `data.amount.currency`.
- Bases: sandbox `https://sandboxapi.opaycheckout.com`, live `https://api.opaycheckout.com` (configurable; the docs disagree on the sandbox host).
- Callbacks: body `{ payload: { reference, status, amount, currency, ... }, sha512, type }`. The documented signature scheme is inconsistent across pages.

## Design

1. **Provider seam.** A plan-billing gateway resolver: `PaystackGateway` and a new `OpayGateway` both implement `IPaymentGateway` (plus a status lookup returning the provider's status, amount and currency). `BillingService` asks the resolver for the configured provider at checkout and for `payment.Provider` when verifying. The existing single `IPaymentGateway` registration used by courses, boosts and stays stays as it is.
2. **Checkout.** `POST /api/billing/checkout` is unchanged for the web: `{ planCode }` in, `{ authorizationUrl }` out. With OPay, the `Payment` row records `Provider = "opay"` and the URL is OPay's `cashierUrl`. Amount and currency come from the plan on the server.
3. **Confirmation, never from a callback alone.** A plan activates only after LinkSuite asks the provider for the payment's status and the amount and currency match. Three triggers call the same confirm path (idempotent, keyed by our reference):
   - the OPay callback `POST /api/webhooks/opay` (anonymous, rate limited): reads the reference, then checks status with OPay; the body's own status and signature are never trusted;
   - a new return check `POST /api/billing/verify { reference }` (signed-in, the payment's own tenant only), which also covers Paystack, so all seven billing pages confirm on return;
   - a background re-check of Pending plan payments (throttled, for 24 hours; then Abandoned).
4. **Web.** All seven billing pages call the return check when they come back with `?ref=`, and show "Payment received, your plan is active", "Still confirming your payment" (with automatic re-checks) or "Payment didn't go through". Copy says "secure checkout", not "Paystack".
5. **Admin console.** The payments list shows the provider.
6. **Config.** `Opay:MerchantId`, `Opay:PublicKey`, `Opay:SecretKey`, `Opay:BaseUrl`; `Billing:PlanProvider`. Env and compose entries added; secrets never logged or returned. `paymentsEnabled` reflects the configured provider.
7. **Dev and tests.** A fake OPay in the test fixture; a local fixture mode for click-throughs in Development (like Paystack's).

## Failure handling

- Unknown reference on a callback: logged, 200.
- Status check fails (network): the payment stays Pending; the return check says "still confirming"; the re-check retries.
- Amount or currency mismatch: payment marked Failed, plan unchanged, logged.
- Provider not configured: checkout returns 503 with a clear sentence, upgrade buttons disabled.

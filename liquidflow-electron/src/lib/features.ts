export const WORKSPACES_ENABLED = import.meta.env.VITE_WORKSPACES_ENABLED === "true";

export const SHARING_ENABLED = import.meta.env.VITE_SHARING_ENABLED === "true";

// LiquidFlow runs local & free: no accounts, billing, subscriptions, or referrals.
// Everything on-device works without signing in. Set VITE_LOCAL_ONLY=false to
// re-enable the upstream cloud/account surfaces.
export const LOCAL_ONLY = import.meta.env.VITE_LOCAL_ONLY !== "false";

// Cloud account features are the inverse of local-only.
export const ACCOUNTS_ENABLED = !LOCAL_ONLY;
export const BILLING_ENABLED = !LOCAL_ONLY;
export const REFERRALS_ENABLED = !LOCAL_ONLY;

# Copilot Instructions

## Project Guidelines
- User preference / correction: `extension{...}` syntax is valid in C# 14 and compiles; bracket array shorthand like `[LockKey(key)]` is valid and equals `new RedisKey[] { LockKey(key) }`; `IdempotencyOptions` is registered as a singleton and therefore injectable into `SingleFlight`.
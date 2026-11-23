# Self-issued JWTs protect the localhost API

The Engine's REST API listens only on 127.0.0.1, but any process on the Mac could call it and read the user's files through it, so every request must carry a short-lived JWT. At first start the Engine creates a signing key and a bootstrap secret in a file only the user can read; the Launcher and CLI exchange that secret for a JWT at `POST /auth/token`.

## Considered Options

- **No authentication**: rejected; any local app could search the user's files.
- **One static token with no expiry**: simpler and adequate for a single user, but rejected because a leaked token stays valid forever and the JWT flow with Spring Security is deliberately part of what the project demonstrates.

package dev.pounce.api;

import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.security.oauth2.jose.jws.MacAlgorithm;
import org.springframework.security.oauth2.jwt.JwsHeader;
import org.springframework.security.oauth2.jwt.JwtClaimsSet;
import org.springframework.security.oauth2.jwt.JwtEncoder;
import org.springframework.security.oauth2.jwt.JwtEncoderParameters;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RestController;

import java.time.Instant;

/** Exchanges the bootstrap secret for a short-lived JWT. */
@RestController
class AuthController {

    record TokenRequest(String secret) {
    }

    record TokenResponse(String token, long expiresInSeconds) {
    }

    private final Credentials credentials;
    private final JwtEncoder encoder;
    private final PounceProperties properties;

    AuthController(Credentials credentials, JwtEncoder encoder, PounceProperties properties) {
        this.credentials = credentials;
        this.encoder = encoder;
        this.properties = properties;
    }

    @PostMapping("/auth/token")
    ResponseEntity<TokenResponse> token(@RequestBody TokenRequest request) {
        if (!credentials.isBootstrapSecret(request.secret())) {
            return ResponseEntity.status(HttpStatus.UNAUTHORIZED).build();
        }
        Instant now = Instant.now();
        var claims = JwtClaimsSet.builder()
                .issuer("pounce")
                .subject("local-user")
                .issuedAt(now)
                .expiresAt(now.plus(properties.tokenLifetime()))
                .build();
        String token = encoder.encode(JwtEncoderParameters.from(JwsHeader.with(MacAlgorithm.HS256).build(), claims))
                .getTokenValue();
        return ResponseEntity.ok(new TokenResponse(token, properties.tokenLifetime().toSeconds()));
    }
}

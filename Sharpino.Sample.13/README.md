# Reservation pattern for user registration (Sharpino.Sample.13)

This sample explores a concurrency-safe approach to user registration using a short-lived reservation (claim) pattern.

## Problem
- A user tries to register.
- If the user already exists, registration must fail.
- If the user does not exist, add a claim for the chosen nickname.
- The claim operation must fail if another claim for the same nickname already exists.
- Run the initialization and the claim command together; on exit, claims should be freed.

## Why this helps
- If a user with the same nickname already exists, no initialization is attempted.
- If a user with the same nickname does not exist:
  - Claim the username (fails if a claim already exists for that nickname).
  - Run user initialization along with the claim command.
  - On exit, free the claim.

## Reservation aggregate
- The Reservation aggregate holds a short‑lived list of claims (nickname reservations).

## Tests
- Includes a test that attempts to register the same nickname 10 times in parallel and verifies that only one registration succeeds.

## Todo
- Add a timestamp to each claim so old claims are wiped automatically (clean any old claim whenever a new claim is added).

## Notes
- Startup may suffer while fetching all users if they are not cached; a warmup cache usually mitigates this in production.
- See this article for discussion: [The three read models of Event Sourcing (benchmarked in F#)](https://medium.com/@tonyx1/the-three-read-models-of-event-sourcing-benchmarked-in-f-e0f5e5815d89)
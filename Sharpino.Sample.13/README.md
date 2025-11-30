In this example I suggest a way to solve a problem of concurrency

An in progress example of reservation pattern

The problem is the following:
A user try to register 
If the user already exists, the registration should fail
If the user doesn't exist then add a "claim" for it that will fail if a claim with same nickname exists
run the initialization and for claim command
in exiting the claims should be freed

This may solve potential duplicated nicknames because:
if a user with the same nickname already existed, then no try to initialize the new user occurs
if a user with the same nickname doesn't then:
    claim that username (claim with fail if a claim of the same username exists)
    run the initialization and for claim command
    in exiting the claims should be freed

The Reservation will contain a short-lived list of claims

Tests: In a test I try to register a user 10 times in parallel with the same nickname and check that only one registration succeeds

Todo: add a timestamp so that an old claim will be wiped (clean any old claim any time a new claim is added)

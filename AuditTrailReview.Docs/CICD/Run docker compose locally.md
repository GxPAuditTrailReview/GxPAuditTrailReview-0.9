

# Run docker compose locally

These flavors are for evaluation and local development only. See [Deployment Options](../Tech/Deployment%20Options.md) for the other targets and the facts shared by all of them. For a server, see [Run docker compose on VPS](Run%20docker%20compose%20on%20VPS.md).

### How to run AuditTrailReview locally on docker based on docker-compose


Run Docker desktop on your local machine 

- Run Terminal

- Find the path to the repository on your local machine

- From the repository root, run the docker compose command based on the database you want to use (postgres or sqlite)



	- On postgres database:
`docker compose --project-directory . -f docker/docker-compose.postgres.yml up -d`


	- On sqlite database:
`docker compose --project-directory . -f docker/docker-compose.sqlite.yml up --build -d` 
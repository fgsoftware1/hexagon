docker run -it -u 0 --name teamcity-server -v /ci/data:/data/teamcity_server/datadir -v /ci/logs:/opt/teamcity/logs -p 8111:8111 jetbrains/teamcity-server
VERSION=configHardcodeFix_17
echo "rebuilding version ${VERSION} FOR arm64 ONLY..."


echo "FROM --platform=\$BUILDPLATFORM finbarrbrady/streammaster:latest-build AS build" > Dockerfile.sm
cat Dockerfile.sm.template >> Dockerfile.sm

docker build \
  -t finbarrbrady/streammaster:${VERSION}-sm \
  -f Dockerfile.sm \
  . \
  # --push

echo "build completed for image finbarrbrady/streammaster:${VERSION}-sm"

echo "FROM finbarrbrady/streammaster:${VERSION}-sm AS sm" > Dockerfile
echo "FROM finbarrbrady/streammaster:latest-base AS base" >> Dockerfile
cat Dockerfile.template >> Dockerfile

docker build \
  -t finbarrbrady/streammaster:${VERSION} \
  -t finbarrbrady/streammaster:latest \
  -f Dockerfile \
  . \
  --push

echo "build complete with image finbarrbrady/streammaster:${VERSION}"
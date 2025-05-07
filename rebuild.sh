VERSION=configHardcodeFix_11

echo "FROM --platform=\$BUILDPLATFORM finbarrbrady/streammaster:latest-build AS build" > Dockerfile.sm
cat Dockerfile.sm.template >> Dockerfile.sm

docker buildx build \
  --platform linux/amd64,linux/arm64 \
  -t finbarrbrady/streammaster:${VERSION}-sm \
  -f Dockerfile.sm \
  . \
  --push

echo "build completed for image finbarrbrady/streammaster:${VERSION}-sm"

echo "FROM finbarrbrady/streammaster:${VERSION}-sm AS sm" > Dockerfile
echo "FROM finbarrbrady/streammaster:latest-base AS base" >> Dockerfile
cat Dockerfile.template >> Dockerfile

docker buildx build \
  --platform linux/amd64,linux/arm64 \
  -t finbarrbrady/streammaster:${VERSION} \
  -t finbarrbrady/streammaster:latest \
  -f Dockerfile \
  . \
  --push

echo "build complete with image finbarrbrady/streammaster:${VERSION}"
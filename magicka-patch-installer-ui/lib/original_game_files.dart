import 'dart:io';

import 'package:crypto/crypto.dart';

class OriginalGameFileSpec {
  const OriginalGameFileSpec({
    required this.fileName,
    required this.size,
    required this.sha256,
  });

  final String fileName;
  final int size;
  final String sha256;
}

class OriginalGameFileCatalog {
  // Keep this catalog in sync with
  // docs/injected-source/Magicka.CommunityPatch/OriginalBackupAudit.cs.
  static const id = 'steam_build_4143032';

  static const magicka = OriginalGameFileSpec(
    fileName: 'Magicka.exe',
    size: 3524096,
    sha256: 'a896e05a3cff65cf9bab4e67e13ae72cb428d99aa93098cf6a8dd8cbc3112ee7',
  );

  static const polygonHead = OriginalGameFileSpec(
    fileName: 'PolygonHead.dll',
    size: 560128,
    sha256: 'b43450b31ba5865db85b9589d7d9ac679d9c1d365b54c6521198b431603cc514',
  );
}

class OriginalBackupFiles {
  const OriginalBackupFiles({this.magicka, this.polygonHead});

  final String? magicka;
  final String? polygonHead;

  bool get complete => magicka != null && polygonHead != null;
}

class OriginalGameFileStore {
  const OriginalGameFileStore({
    this.magickaSpec = OriginalGameFileCatalog.magicka,
    this.polygonHeadSpec = OriginalGameFileCatalog.polygonHead,
  });

  final OriginalGameFileSpec magickaSpec;
  final OriginalGameFileSpec polygonHeadSpec;

  Future<bool> matches(String path, OriginalGameFileSpec spec) async {
    try {
      final file = File(path);
      if (!await file.exists() || await file.length() != spec.size) {
        return false;
      }
      final digest = await sha256.bind(file.openRead()).first;
      return digest.toString().toLowerCase() == spec.sha256.toLowerCase();
    } catch (_) {
      return false;
    }
  }

  Future<OriginalBackupFiles> resolve({
    required String gameDirectory,
    required String backupDirectory,
    String? manifestMagickaPath,
    String? manifestPolygonHeadPath,
  }) async {
    await Directory(backupDirectory).create(recursive: true);
    final magicka = await _resolveOne(
      gameDirectory: gameDirectory,
      backupDirectory: backupDirectory,
      manifestPath: manifestMagickaPath,
      spec: magickaSpec,
    );
    final polygonHead = await _resolveOne(
      gameDirectory: gameDirectory,
      backupDirectory: backupDirectory,
      manifestPath: manifestPolygonHeadPath,
      spec: polygonHeadSpec,
    );
    return OriginalBackupFiles(magicka: magicka, polygonHead: polygonHead);
  }

  Future<String?> _resolveOne({
    required String gameDirectory,
    required String backupDirectory,
    required OriginalGameFileSpec spec,
    String? manifestPath,
  }) async {
    final candidates = <String>[];
    final seen = <String>{};

    void addCandidate(String? path) {
      final value = path?.trim() ?? '';
      if (value.isEmpty) return;
      final key = File(value).absolute.path.toLowerCase();
      if (seen.add(key)) candidates.add(value);
    }

    addCandidate(manifestPath);
    addCandidate(_join(backupDirectory, '${spec.fileName}.original'));

    try {
      var count = 0;
      await for (final entity in Directory(
        backupDirectory,
      ).list(recursive: true, followLinks: false)) {
        if (entity is! File) continue;
        addCandidate(entity.path);
        count++;
        if (count >= 4096) break;
      }
    } catch (_) {}

    try {
      await for (final entity in Directory(
        gameDirectory,
      ).list(followLinks: false)) {
        if (entity is! File) continue;
        final name = _baseName(entity.path);
        if (name.toLowerCase() == spec.fileName.toLowerCase()) continue;
        if (_isLikelyManualBackup(name)) addCandidate(entity.path);
      }
    } catch (_) {}

    addCandidate(_join(gameDirectory, spec.fileName));

    for (final candidate in candidates) {
      if (!await matches(candidate, spec)) continue;
      if (_isInside(candidate, backupDirectory)) return candidate;
      return _copyToBackup(candidate, backupDirectory, spec);
    }
    return null;
  }

  Future<String> _copyToBackup(
    String sourcePath,
    String backupDirectory,
    OriginalGameFileSpec spec,
  ) async {
    var destination = File(_join(backupDirectory, '${spec.fileName}.original'));
    var index = 1;
    while (await destination.exists()) {
      if (await matches(destination.path, spec)) return destination.path;
      destination = File(
        _join(backupDirectory, '${spec.fileName}.original.$index'),
      );
      index++;
    }

    final temporary = File(
      '${destination.path}.new.$pid.${DateTime.now().microsecondsSinceEpoch}',
    );
    try {
      await File(sourcePath).copy(temporary.path);
      if (!await matches(temporary.path, spec)) {
        throw FileSystemException(
          'The copied original file failed verification.',
          temporary.path,
        );
      }
      await temporary.rename(destination.path);
      return destination.path;
    } finally {
      try {
        if (await temporary.exists()) await temporary.delete();
      } catch (_) {}
    }
  }

  static bool _isLikelyManualBackup(String fileName) {
    final value = fileName.toLowerCase();
    return value.contains('original') ||
        value.contains('backup') ||
        value.contains('copy') ||
        value.endsWith('.bak') ||
        value.contains('.bak.') ||
        value.endsWith('.old');
  }

  static bool _isInside(String filePath, String directoryPath) {
    final file = File(filePath).absolute.path.toLowerCase();
    var directory = Directory(directoryPath).absolute.path.toLowerCase();
    if (!directory.endsWith(Platform.pathSeparator)) {
      directory += Platform.pathSeparator;
    }
    return file.startsWith(directory);
  }

  static String _baseName(String path) {
    final parts = path.split(RegExp(r'[\\/]+'));
    return parts.isEmpty ? path : parts.last;
  }

  static String _join(String directory, String name) {
    if (directory.endsWith(Platform.pathSeparator)) return '$directory$name';
    return '$directory${Platform.pathSeparator}$name';
  }
}

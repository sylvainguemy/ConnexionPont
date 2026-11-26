# Validation

## Vérifié dans l'environnement de développement

- Compilation des deux interfaces Windows avec le compilateur .NET Framework intégré.
- Génération de certificats RSA, empreintes correctes et rejet d'une empreinte différente.
- Export/import du profil chiffré, absence de clé privée relais dans l'export.
- Refus d'un mauvais mot de passe, d'un profil modifié et d'un profil trop volumineux.
- Enregistrement / lecture de la configuration protégée DPAPI.

## Tests réseau automatisés

`scripts/test.ps1` lance le parcours TLS complet avec deux services TCP de test, vérifie l'intégrité binaire, la séparation des routes, les sessions concurrentes, le rejet d'un agent non appairé et d'une session inconnue, la fermeture, puis la reconnexion.

**Résultat : 20 contrôles réussis sur Windows Server 2025, le 26 novembre 2026.** [Exécution GitHub vérifiée](https://github.com/sylvainguemy/ConnexionPont/actions/runs/45773859635), pour les sources du commit `329ee312080bec34b1b780bf2f08591c341b8741`.

Le parcours vérifié comprend l'authentification mutuelle TLS, deux transferts binaires de 1 Mio sur les routes RDP et SSH, quatre flux simultanés distincts, la libération des sessions, le rejet d'une session inconnue et d'un agent non appairé, l'arrêt du relais, la reconnexion automatique et l'utilisation de SSH après reprise.

L'environnement local de développement bloque les connexions TCP, même vers la boucle locale (`WSAEACCES`) ; ces tests réseau ont donc été exécutés sur le runner GitHub Windows. Le workflow ne publie son ZIP que si tous les tests passent.

Les deux interfaces WinForms ont également été construites et rendues localement ; le rendu visuel a été contrôlé. Le test optionnel `scripts/test-ui.ps1` permet de reproduire ce contrôle (il ouvre brièvement les deux fenêtres et écrit les images dans `artifacts`, hors livraison).

## Qualification à effectuer chez l'utilisateur

- Freebox : plage IPv4 / full-stack, redirection, pare-feu, connexion depuis un réseau externe.
- PC client : édition Windows compatible RDP, service, autorisations et compte Windows.
- Serveur SSH : adresse interne réelle, port 22 joignable depuis le PC client, authentification et empreinte du serveur.
- RDP et SSH réels, veille / réveil, réseau du client et éventuel proxy / inspection TLS.
- Installation et désinstallation avec les droits du poste cible ; EXE non signés et politiques locales.

V0.1 : pas d'audit indépendant et pas de validation de production implicite.
